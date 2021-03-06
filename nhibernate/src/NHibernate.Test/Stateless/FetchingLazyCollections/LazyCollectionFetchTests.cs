using System;
using System.Collections.Generic;
using Iesi.Collections.Generic;
using NHibernate.Cfg.MappingSchema;
using NHibernate.Mapping.ByCode;
using NHibernate.Mapping.ByCode.Conformist;
using NUnit.Framework;
using SharpTestsEx;

namespace NHibernate.Test.Stateless.FetchingLazyCollections
{
	public class LazyCollectionFetchTests : TestCaseMappingByCode
	{
		protected override HbmMapping GetMappings()
		{
			var mapper = new ModelMapper();
			mapper.BeforeMapClass += (mi, t, cm) => cm.Id(im => im.Generator(Generators.HighLow));
			mapper.Class<Animal>(mc =>
			                     {
			                     	mc.Id(x => x.Id);
			                     	mc.Discriminator(dm => dm.Column("kind"));
			                     	mc.Property(x => x.Description);
			                     });
			mapper.Subclass<Reptile>(mc => { mc.Property(x => x.BodyTemperature); });
			mapper.Subclass<Human>(mc =>
			                       {
			                       	mc.Property(x => x.Name);
			                       	mc.Property(x => x.NickName);
			                       	mc.Property(x => x.Birthdate, pm => pm.Type(NHibernateUtil.Date));
			                       });
			mapper.AddMapping<FamilyMap<Reptile>>();
			mapper.AddMapping<FamilyMap<Human>>();
			var mappings = mapper.CompileMappingForAllExplicitAddedEntities();
			return mappings;
		}

		#region Nested type: FamilyMap

		private class FamilyMap<T> : ClassMapping<Family<T>> where T : Animal
		{
			public FamilyMap()
			{
				string familyOf = typeof (T).Name;

				Id(x => x.Id);
				EntityName(familyOf + "Family");
				Table("Families");
				Discriminator(dm => dm.Column("familyKind"));
				DiscriminatorValue(familyOf);
				Where(string.Format("familyKind = '{0}'", familyOf));
				ManyToOne(x => x.Father, map =>
				                         {
				                         	map.Lazy(LazyRelation.NoLazy);
				                         	map.Class(typeof (T));
				                         	map.Cascade(Mapping.ByCode.Cascade.All);
				                         });
				ManyToOne(x => x.Mother, map =>
				                         {
				                         	map.Lazy(LazyRelation.NoLazy);
				                         	map.Class(typeof (T));
				                         	map.Cascade(Mapping.ByCode.Cascade.All);
				                         });
				Set(x => x.Childs, cam =>
				                   {
				                   	cam.Key(km => km.Column("familyId"));
				                   	cam.Cascade(Mapping.ByCode.Cascade.All);
				                   },
				    rel => rel.OneToMany());
			}
		}

		#endregion

		[Test]
		public void ShouldWorkLoadingComplexEntities()
		{
			const string crocodileFather = "Crocodile father";
			const string crocodileMother = "Crocodile mother";

			using (ISession s = sessions.OpenSession())
			using (ITransaction tx = s.BeginTransaction())
			{
				var rf = new Reptile { Description = crocodileFather };
				var rm = new Reptile { Description = crocodileMother };
				var rc1 = new Reptile { Description = "Crocodile" };
				var rc2 = new Reptile { Description = "Crocodile" };
				var rfamily = new Family<Reptile>
				{
					Father = rf,
					Mother = rm,
					Childs = new HashedSet<Reptile> { rc1, rc2 }
				};
				s.Save("ReptileFamily", rfamily);
				tx.Commit();
			}

			const string humanFather = "Fred";
			const string humanMother = "Wilma";
			using (ISession s = sessions.OpenSession())
			using (ITransaction tx = s.BeginTransaction())
			{
				var hf = new Human { Description = "Flinstone", Name = humanFather };
				var hm = new Human { Description = "Flinstone", Name = humanMother };
				var hc1 = new Human { Description = "Flinstone", Name = "Pebbles" };
				var hfamily = new Family<Human>
				{
					Father = hf,
					Mother = hm,
					Childs = new HashedSet<Human> { hc1 }
				};
				s.Save("HumanFamily", hfamily);
				tx.Commit();
			}

			using (IStatelessSession s = sessions.OpenStatelessSession())
			using (ITransaction tx = s.BeginTransaction())
			{
				IList<Family<Human>> hf = s.CreateQuery("from HumanFamily").List<Family<Human>>();
				Assert.That(hf.Count, Is.EqualTo(1));
				Assert.That(hf[0].Father.Name, Is.EqualTo(humanFather));
				Assert.That(hf[0].Mother.Name, Is.EqualTo(humanMother));
				NHibernateUtil.IsInitialized(hf[0].Childs).Should("Lazy collection should NOT be initialized").Be.False();

				IList<Family<Reptile>> rf = s.CreateQuery("from ReptileFamily").List<Family<Reptile>>();
				Assert.That(rf.Count, Is.EqualTo(1));
				Assert.That(rf[0].Father.Description, Is.EqualTo(crocodileFather));
				Assert.That(rf[0].Mother.Description, Is.EqualTo(crocodileMother));
				NHibernateUtil.IsInitialized(hf[0].Childs).Should("Lazy collection should NOT be initialized").Be.False();

				tx.Commit();
			}

			using (ISession s = sessions.OpenSession())
			using (ITransaction tx = s.BeginTransaction())
			{
				s.Delete("from HumanFamily");
				s.Delete("from ReptileFamily");
				tx.Commit();
			}
		}
	}
}