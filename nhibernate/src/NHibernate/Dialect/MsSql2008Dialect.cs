﻿using System.Data;
using NHibernate.Dialect.Function;

namespace NHibernate.Dialect
{
	public class MsSql2008Dialect : MsSql2005Dialect
	{
		protected override void RegisterDateTimeTypeMappings()
		{
			base.RegisterDateTimeTypeMappings();
			RegisterColumnType(DbType.DateTime2, "DATETIME2");
			RegisterColumnType(DbType.DateTimeOffset, "DATETIMEOFFSET");
			RegisterColumnType(DbType.Date, "DATE");
			RegisterColumnType(DbType.Time, "TIME");
		}

		protected override void RegisterFunctions()
		{
			base.RegisterFunctions();
			RegisterFunction("current_timestamp", new NoArgSQLFunction("sysdatetime", NHibernateUtil.DateTime2, true));
			RegisterFunction("current_timestamp_offset", new NoArgSQLFunction("sysdatetimeoffset", NHibernateUtil.DateTimeOffset, true));
		}

		protected override void RegisterKeywords()
		{
			base.RegisterKeywords();
			RegisterKeyword("datetime2");
			RegisterKeyword("datetimeoffset");
		}
	}
}