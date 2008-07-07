using System;
using System.Collections.Generic;
using System.Configuration;
using System.Reflection;
using log4net;
using NHibernate.Bytecode;
using NHibernate.Util;
using NHibernate.Cfg.ConfigurationSchema;

namespace NHibernate.Cfg
{
	/// <summary>
	/// Provides access to configuration information.
	/// </summary>
	/// <remarks>
	/// NHibernate has two property scopes:
	/// <list>
	///		<item><description>
	///		 Factory-level properties may be passed to the <see cref="ISessionFactory" /> when it is
	///		 instantiated. Each instance might have different property values. If no properties are
	///		 specified, the factory gets them from Environment
	///		</description></item>
	///		<item><description>
	///		 System-level properties are shared by all factory instances and are always determined
	///		 by the <see cref="Cfg.Environment" /> properties
	///		</description></item>
	/// </list>
	/// In NHibernate, <c>&lt;hibernate-configuration&gt;</c> section in the application configuration file
	/// corresponds to Java system-level properties; <c>&lt;session-factory&gt;</c>
	/// section is the session-factory-level configuration. 
	/// 
	/// It is possible to use the applicatoin configuration file (App.config) together with the NHibernate 
	/// configuration file (hibernate.cfg.xml) at the same time.
	/// Properties in hibernate.cfg.xml override/merge properties in applicatoin configuration file where same
	/// property is found. For others configuration a merge is applied.
	/// </remarks>
	public sealed class Environment
	{
		private static string cachedVersion;

		/// <summary>
		/// NHibernate version (informational).
		/// </summary>
		public static string Version
		{
			get
			{
				if (cachedVersion == null)
				{
					Assembly thisAssembly = Assembly.GetExecutingAssembly();
					AssemblyInformationalVersionAttribute[] attrs = (AssemblyInformationalVersionAttribute[])
					                                                thisAssembly.GetCustomAttributes(
					                                                	typeof(AssemblyInformationalVersionAttribute), false);

					if (attrs != null && attrs.Length > 0)
					{
						cachedVersion = string.Format("{0} ({1})", thisAssembly.GetName().Version, attrs[0].InformationalVersion);
					}
					else
					{
						cachedVersion = thisAssembly.GetName().Version.ToString();
					}
				}
				return cachedVersion;
			}
		}

		public const string ConnectionProvider = "connection.provider";
		public const string ConnectionDriver = "connection.driver_class";
		public const string ConnectionString = "connection.connection_string";
		public const string Isolation = "connection.isolation";
		public const string ReleaseConnections = "connection.release_mode";

		/// <summary>
		/// Used to find the .Net 2.0 named connection string
		/// </summary>
		public const string ConnectionStringName = "connection.connection_string_name";

		// Unused, Java-specific
		public const string SessionFactoryName = "session_factory_name";

		public const string Dialect = "dialect";
		/// <summary> A default database schema (owner) name to use for unqualified tablenames</summary>
		public const string DefaultSchema = "default_schema";
		/// <summary> A default database catalog name to use for unqualified tablenames</summary>
		public const string DefaultCatalog = "default_catalog";

		public const string ShowSql = "show_sql";
		public const string MaxFetchDepth = "max_fetch_depth";
		public const string CurrentSessionContextClass = "current_session_context_class";

		// Unused, Java-specific
		public const string UseGetGeneratedKeys = "jdbc.use_get_generated_keys";

		// Unused, not implemented
		public const string StatementFetchSize = "jdbc.fetch_size";

		public const string BatchVersionedData = "jdbc.batch_versioned_data";

		// Unused, not implemented
		public const string OutputStylesheet = "xml.output_stylesheet";

		public const string TransactionStrategy = "transaction.factory_class";

		// Unused, not implemented (and somewhat Java-specific)
		public const string TransactionManagerStrategy = "transaction.manager_lookup_class";

		public const string CacheProvider = "cache.provider_class";
		public const string UseQueryCache = "cache.use_query_cache";
		public const string QueryCacheFactory = "cache.query_cache_factory";
		public const string UseSecondLevelCache = "cache.use_second_level_cache";
		public const string CacheRegionPrefix = "cache.region_prefix";
		public const string UseMinimalPuts = "cache.use_minimal_puts";
		public const string QuerySubstitutions = "query.substitutions";

		/// <summary> Should named queries be checked during startup (the default is enabled). </summary>
		/// <remarks>Mainly intended for test environments.</remarks>
		public const string QueryStartupChecking = "query.startup_check";

		/// <summary> Enable statistics collection</summary>
		public const string GenerateStatistics = "generate_statistics";

		public const string UseIdentifierRollBack = "use_identifier_rollback";

		// The classname of the HQL query parser factory
		public const string QueryTranslator = "query.factory_class";

		// Unused, not implemented
		public const string QueryImports = "query.imports";
		public const string Hbm2ddlAuto = "hbm2ddl.auto";

		// Unused, not implemented
		public const string SqlExceptionConverter = "sql_exception_converter";

		public const string WrapResultSets = "adonet.wrap_result_sets";
		public const string BatchSize = "adonet.batch_size";
		public const string BatchStrategy = "adonet.factory_class";

		// NHibernate-specific properties
		public const string PrepareSql = "prepare_sql";
		public const string CommandTimeout = "command_timeout";

		public const string PropertyBytecodeProvider = "bytecode.provider";
		public const string PropertyUseReflectionOptimizer = "use_reflection_optimizer";

		public const string UseProxyValidator = "use_proxy_validator";
		public const string ProxyFactoryFactoryClass = "proxyfactory.factory_class";

		public const string DefaultBatchFetchSize = "default_batch_fetch_size";

		private static readonly Dictionary<string, string> GlobalProperties;

		private static IBytecodeProvider BytecodeProviderInstance;
		private static bool EnableReflectionOptimizer;

		private static readonly ILog log = LogManager.GetLogger(typeof(Environment));

		/// <summary>
		/// Issue warnings to user when any obsolete property names are used.
		/// </summary>
		/// <param name="props"></param>
		/// <returns></returns>
		public static void VerifyProperties(IDictionary<string, string> props)
		{
		}

		static Environment()
		{
			// Computing the version string is a bit expensive, so do it only if logging is enabled.
			if (log.IsInfoEnabled)
			{
				log.Info("NHibernate " + Version);
			}

			GlobalProperties = new Dictionary<string, string>();
			GlobalProperties[PropertyUseReflectionOptimizer] = bool.TrueString;
			LoadGlobalPropertiesFromAppConfig();
			VerifyProperties(GlobalProperties);

			BytecodeProviderInstance = BuildBytecodeProvider(GlobalProperties);
			EnableReflectionOptimizer = PropertiesHelper.GetBoolean(PropertyUseReflectionOptimizer, GlobalProperties);

			if (EnableReflectionOptimizer)
			{
				log.Info("Using reflection optimizer");
			}
		}

		private static void LoadGlobalPropertiesFromAppConfig()
		{
			object config = ConfigurationManager.GetSection(CfgXmlHelper.CfgSectionName);

			if (config == null)
			{
				log.Info(string.Format("{0} section not found in application configuration file",CfgXmlHelper.CfgSectionName));
				return;
			}

			IHibernateConfiguration NHconfig = config as IHibernateConfiguration;
			if (NHconfig == null)
			{
				log.Info(string.Format("{0} section handler, in application configuration file, is not IHibernateConfiguration, section ignored", CfgXmlHelper.CfgSectionName));
				return;
			}

			GlobalProperties[PropertyBytecodeProvider] = CfgXmlHelper.ByteCodeProviderToString(NHconfig.ByteCodeProviderType);
			GlobalProperties[PropertyUseReflectionOptimizer] = NHconfig.UseReflectionOptimizer.ToString();
			foreach (KeyValuePair<string, string> kvp in NHconfig.SessionFactory.Properties)
			{
				GlobalProperties[kvp.Key] = kvp.Value;
			}
		}

		internal static void ResetSessionFactoryProperties()
		{
			string savedBytecodeProvider = null;
			string savedUseReflectionOptimizer = null;

			// Save values loaded and used in static constructor
			if (GlobalProperties.ContainsKey(PropertyBytecodeProvider))
				savedBytecodeProvider = GlobalProperties[PropertyBytecodeProvider];
			if (GlobalProperties.ContainsKey(PropertyUseReflectionOptimizer))
				savedUseReflectionOptimizer = GlobalProperties[PropertyUseReflectionOptimizer];
			// Clean all property loaded from app.config
			GlobalProperties.Clear();

			// Restore values loaded and used in static constructor
			if (savedBytecodeProvider != null)
				GlobalProperties[PropertyBytecodeProvider] = savedBytecodeProvider;

			if (savedUseReflectionOptimizer != null)
				GlobalProperties[PropertyUseReflectionOptimizer] = savedUseReflectionOptimizer;
		}

		private Environment()
		{
			// should not be created.	
		}

		/// <summary>
		/// Gets a copy of the configuration found in <c>&lt;hibernate-configuration&gt;</c> section
		/// of app.config/web.config.
		/// </summary>
		/// <remarks>
		/// This is the replacement for hibernate.properties
		/// </remarks>
		public static IDictionary<string, string> Properties
		{
			get { return new Dictionary<string,string>(GlobalProperties); }
		}

		[Obsolete]
		public static bool UseStreamsForBinary
		{
			get { return true; }
		}

		/// <summary>
		/// The bytecode provider to use.
		/// </summary>
		/// <remarks>
		/// This property is read from the <c>&lt;nhibernate&gt;</c> section
		/// of the application configuration file by default. Since it is not
		/// always convenient to configure NHibernate through the application
		/// configuration file, it is also possible to set the property value
		/// manually. This should only be done before a configuration object
		/// is created, otherwise the change may not take effect.
		/// </remarks>
		public static IBytecodeProvider BytecodeProvider
		{
			get { return BytecodeProviderInstance; }
			set { BytecodeProviderInstance = value; }
		}

		/// <summary>
		/// Whether to enable the use of reflection optimizer
		/// </summary>
		/// <remarks>
		/// This property is read from the <c>&lt;nhibernate&gt;</c> section
		/// of the application configuration file by default. Since it is not
		/// always convenient to configure NHibernate through the application
		/// configuration file, it is also possible to set the property value
		/// manually. This should only be done before a configuration object
		/// is created, otherwise the change may not take effect.
		/// </remarks>
		public static bool UseReflectionOptimizer
		{
			get { return EnableReflectionOptimizer; }
			set { EnableReflectionOptimizer = value; }
		}

		public static IBytecodeProvider BuildBytecodeProvider(IDictionary<string, string> properties)
		{
			string defaultBytecodeProvider = "lcg";
			string provider = PropertiesHelper.GetString(PropertyBytecodeProvider, properties,
			                                             defaultBytecodeProvider);
			log.Info("Bytecode provider name : " + provider);
			return BuildBytecodeProvider(provider);
		}

		private static IBytecodeProvider BuildBytecodeProvider(string providerName)
		{
			switch (providerName)
			{
				case "codedom":
					return new Bytecode.CodeDom.BytecodeProviderImpl();
				case "lcg":
					return new Bytecode.Lightweight.BytecodeProviderImpl();
				case "null":
					return new NullBytecodeProvider();
				default:
					log.Warn("unrecognized bytecode provider [" + providerName + "], using null by default");
					return new NullBytecodeProvider();
			}
		}
	}
}
