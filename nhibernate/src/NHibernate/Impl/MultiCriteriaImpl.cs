using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using Iesi.Collections;
using Iesi.Collections.Generic;
using log4net;
using NHibernate.Cache;
using NHibernate.Engine;
using NHibernate.Criterion;
using NHibernate.Loader.Criteria;
using NHibernate.SqlCommand;
using NHibernate.SqlTypes;
using NHibernate.Transform;
using NHibernate.Type;

namespace NHibernate.Impl
{
	public class MultiCriteriaImpl : IMultiCriteria
	{
		private static readonly ILog log = LogManager.GetLogger(typeof(MultiCriteriaImpl));
		private readonly IList<ICriteria> criteriaQueries = new List<ICriteria>();

		private readonly SessionImpl session;
		private readonly ISessionFactoryImplementor factory;
		private readonly List<CriteriaQueryTranslator> translators = new List<CriteriaQueryTranslator>();
		private readonly List<QueryParameters> parameters = new List<QueryParameters>();
		private readonly List<SqlType> types = new List<SqlType>();
		private SqlString sqlString = new SqlString();
		private readonly List<CriteriaLoader> loaders = new List<CriteriaLoader>();
		private readonly Dialect.Dialect dialect;
		private IList criteriaResults;
		private readonly Dictionary<string, int> criteriaResultPositions = new Dictionary<string, int>();
		private bool isCacheable = false;
		private bool forceCacheRefresh = false;
		private string cacheRegion;
		private IResultTransformer resultTransformer;
		private readonly Dictionary<CriteriaLoader, int> loaderToResultIndex = new Dictionary<CriteriaLoader, int>();

		/// <summary>
		/// Initializes a new instance of the <see cref="MultiCriteriaImpl"/> class.
		/// </summary>
		/// <param name="session">The session.</param>
		/// <param name="factory">The factory.</param>
		internal MultiCriteriaImpl(SessionImpl session, ISessionFactoryImplementor factory)
		{
			dialect = session.Factory.Dialect;
			if (!session.Factory.ConnectionProvider.Driver.SupportsMultipleQueries)
			{
				throw new NotSupportedException(
					string.Format("The dialect {0} does not support multiple queries.", dialect.GetType().FullName));
			}
			this.session = session;
			this.factory = factory;
		}


		public SqlString SqlString
		{
			get { return sqlString; }
		}

		public IList List()
		{
			bool cacheable = session.Factory.Settings.IsQueryCacheEnabled && isCacheable;

			CreateCriteriaLoaders();
			CombineCriteriaQueries();

			if (log.IsDebugEnabled)
			{
				log.DebugFormat("Multi criteria with {0} criteria queries.", criteriaQueries.Count);
				for (int i = 0; i < criteriaQueries.Count; i++)
				{
					log.DebugFormat("Query #{0}: {1}", i, criteriaQueries[i]);
				}
			}

			if (cacheable)
			{
				criteriaResults = ListUsingQueryCache();
			}
			else
			{
				criteriaResults = ListIgnoreQueryCache();
			}

			return criteriaResults;
		}

		private IList ListUsingQueryCache()
		{
			IQueryCache queryCache = session.Factory.GetQueryCache(cacheRegion);

			ISet filterKeys = FilterKey.CreateFilterKeys(session.EnabledFilters, session.EntityMode);

			ISet<string> querySpaces = new HashedSet<string>();
			ArrayList resultTypesList = new ArrayList();
			int[] maxRows = new int[loaders.Count];
			int[] firstRows = new int[loaders.Count];
			for (int i = 0; i < loaders.Count; i++)
			{
				querySpaces.AddAll(loaders[i].QuerySpaces);
				resultTypesList.Add(loaders[i].ResultTypes);
				firstRows[i] = parameters[i].RowSelection.FirstRow;
				maxRows[i] = parameters[i].RowSelection.MaxRows;
			}

			MultipleQueriesCacheAssembler assembler = new MultipleQueriesCacheAssembler(resultTypesList);
			QueryParameters combinedParameters = CreateCombinedQueryParameters();
			QueryKey key = new QueryKey(session.Factory, SqlString, combinedParameters, filterKeys)
				.SetFirstRows(firstRows)
				.SetMaxRows(maxRows);

			IList result =
				assembler.GetResultFromQueryCache(session,
												  combinedParameters,
												  querySpaces,
												  queryCache,
												  key);

			if (result == null)
			{
				log.Debug("Cache miss for multi criteria query");
				IList list = DoList();
				queryCache.Put(key, new ICacheAssembler[] { assembler }, new object[] { list }, combinedParameters.NaturalKeyLookup, session);
				result = list;
			}

			return GetResultList(result);
		}

		private IList ListIgnoreQueryCache()
		{
			return GetResultList(DoList());
		}

		protected virtual IList GetResultList(IList results)
		{
			if (resultTransformer != null)
			{
				for (int i = 0; i < results.Count; i++)
				{
					results[i] = resultTransformer.TransformList((IList)results[i]);
				}
			}
			else
			{
				for (int i = 0; i < results.Count; i++)
				{
					var critImp = criteriaQueries[i] as CriteriaImpl;
					if(critImp==null || critImp.ResultTransformer==null)
						continue;
					results[i] = critImp.ResultTransformer.TransformList((IList)results[i]);
				}
			}
			return results;
		}

		private IList DoList()
		{
			ArrayList results = new ArrayList();
			GetResultsFromDatabase(results);
			return results;
		}

		private void CombineCriteriaQueries()
		{
			foreach (CriteriaLoader loader in loaders)
			{
				CriteriaQueryTranslator translator = loader.Translator;
				translators.Add(translator);
				QueryParameters queryParameters = translator.GetQueryParameters();
				parameters.Add(queryParameters);
				SqlCommandInfo commandInfo = loader.GetQueryStringAndTypes(session, queryParameters);
				sqlString = sqlString.Append(commandInfo.Text)
					.Append(dialect.MultipleQueriesSeparator)
					.Append(Environment.NewLine);
				types.AddRange(commandInfo.ParameterTypes);
			}
		}

		private void GetResultsFromDatabase(IList results)
		{
			using (
				IDbCommand command =
					session.Batcher.PrepareCommand(CommandType.Text, sqlString, types.ToArray()))
			{
				BindParameters(command);
				ArrayList[] hydratedObjects = new ArrayList[loaders.Count];
				List<EntityKey[]>[] subselectResultKeys = new List<EntityKey[]>[loaders.Count];
				bool[] createSubselects = new bool[loaders.Count];
				IDataReader reader = session.Batcher.ExecuteReader(command);
				try
				{
					for (int i = 0; i < loaders.Count; i++)
					{
						CriteriaLoader loader = loaders[i];
						int entitySpan = loader.EntityPersisters.Length;
						hydratedObjects[i] = entitySpan == 0 ? null : new ArrayList(entitySpan);
						EntityKey[] keys = new EntityKey[entitySpan];
						QueryParameters queryParameters = parameters[i];
						IList tmpResults = new ArrayList();
						RowSelection selection = parameters[i].RowSelection;
						createSubselects[i] = loader.IsSubselectLoadingEnabled;
						subselectResultKeys[i] = createSubselects[i] ? new List<EntityKey[]>() : null;
						int maxRows = Loader.Loader.HasMaxRows(selection) ? selection.MaxRows : int.MaxValue;
						if (!dialect.SupportsLimitOffset || !NHibernate.Loader.Loader.UseLimit(selection, dialect))
						{
							Loader.Loader.Advance(reader, selection);
						}
						int count;
						for (count = 0; count < maxRows && reader.Read(); count++)
						{
							object o =
								loader.GetRowFromResultSet(reader, session, queryParameters, loader.GetLockModes(queryParameters.LockModes),
														   null, hydratedObjects[i], keys, false);
							if (createSubselects[i])
							{
								subselectResultKeys[i].Add(keys);
								keys = new EntityKey[entitySpan]; //can't reuse in this case
							}
							tmpResults.Add(o);
						}
						results.Add(tmpResults);
						reader.NextResult();
					}
				}
				catch (Exception e)
				{
					log.Error("Error executing multi criteria : [" + command.CommandText + "]");
					throw new HibernateException("Error executing multi criteria : [" + command.CommandText + "]", e);
				}
				finally
				{
					session.Batcher.CloseCommand(command, reader);
				}
				for (int i = 0; i < loaders.Count; i++)
				{
					CriteriaLoader loader = loaders[i];
					loader.InitializeEntitiesAndCollections(hydratedObjects[i], reader, session, false);

					if (createSubselects[i])
					{
						loader.CreateSubselects(subselectResultKeys[i], parameters[i], session);
					}
				}
			}
		}

		private void CreateCriteriaLoaders()
		{
			//a criteria can use more than a single query (polymorphic queries), need to have a 
			//way to correlate a loader to a result index
			int criteriaIndex = 0;
			foreach (CriteriaImpl criteria in criteriaQueries)
			{
				string[] implementors = factory.GetImplementors(criteria.EntityOrClassName);
				int size = implementors.Length;

				CriteriaLoader[] tmpLoaders = new CriteriaLoader[size];
				ISet<string> spaces = new HashedSet<string>();

				for (int i = 0; i < size; i++)
				{
					CriteriaLoader loader = new CriteriaLoader(
						session.GetOuterJoinLoadable(implementors[i]),
						factory,
						criteria,
						implementors[i],
						session.EnabledFilters
						);
					tmpLoaders[i] = loader;
					loaderToResultIndex[loader] = criteriaIndex;
					spaces.AddAll(tmpLoaders[i].QuerySpaces);
				}
				loaders.AddRange(tmpLoaders);
				criteriaIndex += 1;
			}
		}

		private void BindParameters(IDbCommand command)
		{
			int colIndex = 0;

			colIndex = BindLimitParametersFirstIfNeccesary(command, colIndex);
			colIndex = BindQueryParameters(command, colIndex);

			BindLimitParametersLastIfNeccesary(command, colIndex);
		}

		private void BindLimitParametersLastIfNeccesary(IDbCommand command, int colIndex)
		{
			for (int i = 0; i < loaders.Count; i++)
			{
				QueryParameters parameter = parameters[i];
				RowSelection selection = parameter.RowSelection;
				if (Loader.Loader.UseLimit(selection, dialect) && !dialect.BindLimitParametersFirst)
				{
					colIndex += Loader.Loader.BindLimitParameters(command, colIndex, selection, session);
				}
			}
		}

		private int BindQueryParameters(IDbCommand command, int colIndex)
		{
			for (int i = 0; i < loaders.Count; i++)
			{
				QueryParameters parameter = parameters[i];
				colIndex += loaders[i].BindPositionalParameters(command, parameter, colIndex, session);
				colIndex += loaders[i].BindNamedParameters(command, parameter.NamedParameters, colIndex, session);
			}
			return colIndex;
		}

		private int BindLimitParametersFirstIfNeccesary(IDbCommand command, int colIndex)
		{
			for (int i = 0; i < loaders.Count; i++)
			{
				QueryParameters parameter = parameters[i];
				RowSelection selection = parameter.RowSelection;
				if (Loader.Loader.UseLimit(selection, dialect) && dialect.BindLimitParametersFirst)
				{
					colIndex += Loader.Loader.BindLimitParameters(command, colIndex, selection, session);
				}
			}
			return colIndex;
		}

		public IMultiCriteria Add(ICriteria criteria)
		{
			criteriaQueries.Add(criteria);
			return this;
		}

		public IMultiCriteria Add(string key, ICriteria criteria)
		{
			ThrowIfKeyAlreadyExists(key);
			criteriaQueries.Add(criteria);
			criteriaResultPositions.Add(key, criteriaQueries.Count - 1);
			return this;
		}

		public IMultiCriteria Add(DetachedCriteria detachedCriteria)
		{
			criteriaQueries.Add(
				detachedCriteria.GetExecutableCriteria(session)
				);
			return this;
		}

		public IMultiCriteria Add(string key, DetachedCriteria detachedCriteria)
		{
			ThrowIfKeyAlreadyExists(key);
			criteriaQueries.Add(detachedCriteria.GetExecutableCriteria(session));
			criteriaResultPositions.Add(key, criteriaQueries.Count-1);
			return this;
		}

		public IMultiCriteria SetCacheable(bool cachable)
		{
			isCacheable = cachable;
			return this;
		}

		public IMultiCriteria ForceCacheRefresh(bool forceRefresh)
		{
			forceCacheRefresh = forceRefresh;
			return this;
		}

		#region IMultiCriteria Members

		public IMultiCriteria SetResultTransformer(IResultTransformer resultTransformer)
		{
			this.resultTransformer = resultTransformer;
			return this;
		}

		public object GetResult(string key)
		{
			if (criteriaResults == null) List();

			if (!criteriaResultPositions.ContainsKey(key))
			{
				throw new InvalidOperationException(String.Format("The key '{0}' is unknown", key));
			}

			return criteriaResults[criteriaResultPositions[key]];
		}

		#endregion

		public IMultiCriteria SetCacheRegion(string cacheRegion)
		{
			this.cacheRegion = cacheRegion;
			return this;
		}

		private QueryParameters CreateCombinedQueryParameters()
		{
			QueryParameters combinedQueryParameters = new QueryParameters();
			combinedQueryParameters.ForceCacheRefresh = forceCacheRefresh;
			combinedQueryParameters.NamedParameters = new Dictionary<string, TypedValue>();
			ArrayList positionalParameterTypes = new ArrayList();
			ArrayList positionalParameterValues = new ArrayList();
			foreach (QueryParameters queryParameters in parameters)
			{
				// There aren't any named params in criteria queries
				//CopyNamedParametersDictionary(combinedQueryParameters.NamedParameters, queryParameters.NamedParameters);
				positionalParameterTypes.AddRange(queryParameters.PositionalParameterTypes);
				positionalParameterValues.AddRange(queryParameters.PositionalParameterValues);
			}
			combinedQueryParameters.PositionalParameterTypes = (IType[])positionalParameterTypes.ToArray(typeof(IType));
			combinedQueryParameters.PositionalParameterValues = (object[])positionalParameterValues.ToArray(typeof(object));
			return combinedQueryParameters;
		}

		private void ThrowIfKeyAlreadyExists(string key)
		{
			if (criteriaResultPositions.ContainsKey(key))
			{
				throw new InvalidOperationException(String.Format("The key '{0}' already exists", key));
			}
		}
	}
}
