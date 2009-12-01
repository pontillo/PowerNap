using System;
using System.Collections.Generic;
using System.Linq;

namespace NHibernate.Cfg.MappingSchema
{
	public partial class HbmElement: IColumnsMapping, IFormulasMapping
	{
		#region Implementation of IColumnsMapping

		public IEnumerable<HbmColumn> Columns
		{
			get { return Items != null ? Items.OfType<HbmColumn>() : AsColumns(); }
		}

		#endregion

		private IEnumerable<HbmColumn> AsColumns()
		{
			if (string.IsNullOrEmpty(column))
			{
				yield break;
			}
			else
			{
				yield return new HbmColumn
				{
					name = column,
					length = length,
					scale = scale,
					precision = precision,
					notnull = notnull,
					unique = unique,
					uniqueSpecified = true,
				};
			}
		}

		#region Implementation of IFormulasMapping

		public IEnumerable<HbmFormula> Formulas
		{
			get { return Items != null ? Items.OfType<HbmFormula>() : AsFormulas(); }
		}

		private IEnumerable<HbmFormula> AsFormulas()
		{
			if (string.IsNullOrEmpty(formula))
			{
				yield break;
			}
			else
			{
				yield return new HbmFormula { Text = new[] { formula } };
			}
		}

		#endregion
	}
}