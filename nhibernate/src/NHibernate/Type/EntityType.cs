using System;
using System.Data;

using NHibernate.Util;
using NHibernate.Engine;
using NHibernate.Persister;

namespace NHibernate.Type
{
	/// <summary>
	/// A reference to an entity class
	/// </summary>
	public abstract class EntityType : AbstractType
	{
		private readonly System.Type persistentClass;
		private readonly bool niceEquals;

		public override sealed bool IsEntityType {
			get { return true; }
        }
	
		public System.Type PersistentClass {
			get { return persistentClass; }
        }

		public override sealed bool Equals(object x, object y) {
			return x==y;
		}

		protected EntityType(System.Type persistentClass) {
			this.persistentClass = persistentClass;
			this.niceEquals = !ReflectHelper.OverridesEquals(persistentClass);
		}
		
		public override object NullSafeGet(IDataReader rs, string name, ISessionImplementor session, object owner) {
		return NullSafeGet( rs, new string[] {name}, session, owner );
		}

		/**
		* This returns the wrong class for an entity with a proxy. Theoretically
		* it should return the proxy class, but it doesn't.
		*/
		public override sealed System.Type ReturnedClass {
			get { return persistentClass; }
        }
	
		protected object GetIdentifier(object value, ISessionImplementor session) {
			return session.GetEntityIdentifierIfNotUnsaved(value);
		}
	
		public override string ToXML(object value, ISessionFactoryImplementor factory) {
			IClassPersister persister = factory.GetPersister(persistentClass);
			return ( value==null ) ? null : persister.IdentifierType.ToXML( persister.GetIdentifier(value), factory );
		}

		public override string Name {
			get { return persistentClass.Name; }
		}
	
		public override object DeepCopy(object value) {
			return value; //special case ... this is the leaf of the containment graph, even though not immutable
		}
	
		public override bool IsMutable {
			get { return false; }
		}

		public abstract bool IsOneToOne { get; }
	
		public override object Disassemble(object value, ISessionImplementor session) {
			if (value==null) {
				return null;
			}
			else {
				object id = session.GetIdentifier(value);
				if (id==null)
					throw new AssertionFailure("cannot cache a reference to an object with a null id");
				return id;
			}
		}
	
		public override object Assemble(object oid, ISessionImplementor session, object owner) {
			return ResolveIdentifier(oid, session, owner);
		}

		public override bool HasNiceEquals {
			get { return niceEquals; }
		}
	
		public override bool IsAssociationType {
			get { return true; }
		}
	
		public override sealed object NullSafeGet(IDataReader rs, string[] names, ISessionImplementor session, object owner) {
			return ResolveIdentifier( Hydrate(rs, names, session, owner), session, owner );
		}
	
		public override abstract object Hydrate(IDataReader rs, string[] names, ISessionImplementor session, object owner);
		
		public override bool IsDirty(object old, object current, ISessionImplementor session) {
			if ( Equals(old, current) ) return false;
		
			object oldid = GetIdentifier(old, session);
			object newid = GetIdentifier(current, session);
			return !session.GetFactory().GetIdentifierType(persistentClass).Equals(oldid, newid);
		}
	}
}