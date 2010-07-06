﻿using System.Linq;
using Remotion.Data.Linq.Clauses.ResultOperators;

namespace NHibernate.Linq.Visitors.ResultOperatorProcessors
{
    public class ProcessFirst : ProcessFirstOrSingleBase, IResultOperatorProcessor<FirstResultOperator>
    {
        public void Process(FirstResultOperator resultOperator, QueryModelVisitor queryModelVisitor, IntermediateHqlTree tree)
        {
            var firstMethod = resultOperator.ReturnDefaultWhenEmpty
                                  ? ReflectionHelper.GetMethod(() => Queryable.FirstOrDefault<object>(null))
                                  : ReflectionHelper.GetMethod(() => Queryable.First<object>(null));

            AddClientSideEval(firstMethod, queryModelVisitor, tree);

            tree.AddAdditionalCriteria((q, p) => q.SetMaxResults(1));
        }
    }
}