using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

using NHibernate.DomainModel.NHSpecific;

using NUnit.Framework;

namespace NHibernate.Test.NHSpecificTest
{
	/// <summary>
	/// Tests for mapping a byte[] Property to a BinaryType.
	/// </summary>
	[TestFixture]
	public class BasicBinaryFixture : TestCase 
	{

		[SetUp]
		public void SetUp() 
		{
			ExportSchema( new string[] { "NHSpecific.BasicBinary.hbm.xml"}, true );
		}


		[Test]
		public void InsertNull() 
		{
			BasicBinary bcBinary = new BasicBinary();
			bcBinary.Id = 1;

			bcBinary.DefaultSize = null;
			bcBinary.WithSize = null;

			ISession s = sessions.OpenSession();
			s.Save(bcBinary);
			s.Flush();
			s.Close();

			s = sessions.OpenSession();
			BasicBinary bcBinaryLoaded = (BasicBinary)s.Load(typeof(BasicBinary), 1);

			Assert.IsNotNull(bcBinaryLoaded);
			Assert.AreEqual(null, bcBinaryLoaded.DefaultSize, "A property mapped as type=\"Byte[]\" with a null byte[] value was not saved & loaded as null");
			Assert.AreEqual(null, bcBinaryLoaded.WithSize, "A property mapped as type=\"Byte[](length)\" with null byte[] value was not saved & loaded as null");

			s.Delete(bcBinaryLoaded);
			s.Flush();
			s.Close();
		}

		[Test]
		public void InsertZeroLength() 
		{
			BasicBinary bcBinary = new BasicBinary();
			bcBinary.Id = 1;

			bcBinary.DefaultSize = new byte[0];
			bcBinary.WithSize = new byte[0];

			ISession s = sessions.OpenSession();
			s.Save(bcBinary);
			s.Flush();
			s.Close();

			s = sessions.OpenSession();
			BasicBinary bcBinaryLoaded = (BasicBinary)s.Load(typeof(BasicBinary), 1);

			Assert.IsNotNull(bcBinaryLoaded);
			Assert.AreEqual(0, bcBinaryLoaded.DefaultSize.Length, "A property mapped as type=\"Byte[]\" with a byte[0] value was not saved & loaded as byte[0]");
			Assert.AreEqual(0, bcBinaryLoaded.WithSize.Length, "A property mapped as type=\"Byte[](length)\" with a byte[0] value was not saved & loaded as byte[0]");

			s.Delete(bcBinaryLoaded);
			s.Flush();
			s.Close();
		}

		[Test]
		public void Insert() 
		{
			BasicBinary bcBinary = Create(1);

			ISession s = sessions.OpenSession();
			s.Save(bcBinary);
			s.Flush();
			s.Close();

			s = sessions.OpenSession();

			BasicBinary bcBinaryLoaded = (BasicBinary)s.Load(typeof(BasicBinary), 1);

			Assert.IsNotNull(bcBinaryLoaded);
			Assert.IsFalse(bcBinary==bcBinaryLoaded);

			ObjectAssert.AssertEquals(bcBinary.DefaultSize, bcBinaryLoaded.DefaultSize);
			ObjectAssert.AssertEquals(bcBinary.WithSize, bcBinaryLoaded.WithSize);

			s.Delete(bcBinaryLoaded);
			s.Flush();
			s.Close();
		}

		[Test]
		public void Update() 
		{
			BasicBinary bcBinary = Create(1);
			
			ISession s = sessions.OpenSession();
			bcBinary.DefaultSize = System.Text.Encoding.UTF8.GetBytes("ghij1`23%$");
			s.Save(bcBinary);
			s.Flush();
			s.Close();

			s = sessions.OpenSession();
			bcBinary = (BasicBinary)s.Load(typeof(BasicBinary), 1);

			bcBinary.DefaultSize[1] = (byte)126;

			s.Flush();
			s.Close();

			s = sessions.OpenSession();
			// make sure the update went through
			bcBinary = (BasicBinary)s.Load(typeof(BasicBinary), 1);

			// was DefaultSize updated
			Assert.AreEqual( 10, bcBinary.DefaultSize.Length );
			Assert.AreEqual( 126, bcBinary.DefaultSize[1] );
			// WithSize should not have been updated
			ObjectAssert.AssertEquals( bcBinary.WithSize, GetByteArray(10) );
			
			// lets modify WithSize
			bcBinary.WithSize = GetByteArray(20);
			s.Flush();
			s.Close();

			s = sessions.OpenSession();
			bcBinary = (BasicBinary)s.Load(typeof(BasicBinary), 1);

			// was DefaultSize not updated
			Assert.AreEqual( 10, bcBinary.DefaultSize.Length );
			Assert.AreEqual( 126, bcBinary.DefaultSize[1] );

			ObjectAssert.AssertEquals( bcBinary.WithSize, GetByteArray(20) );

			s.Delete(bcBinary);
			s.Flush();
			s.Close();
		}

		private BasicBinary Create(int id) 
		{
			BasicBinary bcBinary = new BasicBinary();
			bcBinary.Id = id;

			bcBinary.DefaultSize = GetByteArray(5);
			bcBinary.WithSize = GetByteArray(10);

			return bcBinary;
		}

		private byte[] GetByteArray(int value) 
		{	
			BinaryFormatter bf = new BinaryFormatter();
			MemoryStream stream = new MemoryStream();
			bf.Serialize(stream, value);
			return stream.ToArray();
		}


	}
}
