using Newtonsoft.Json;
using NUnit.Framework;

namespace BuildOnSave.Tests
{
	enum Test1
	{
		A,
		B
	};

	enum Test2
	{
		A,
		B,
		C
	};

	sealed class Settings1
	{
		public Test1 T;
	}

	sealed class Settings2
	{
		public Test2 T;
	}

	[TestFixture]
    public class SettingsTests
    {
		[Test]
		public void NewtonsoftDeserializerDeserializesInvalidEnums()
		{
			var s2 = new Settings2 {T = Test2.C};
			var serialized = JsonConvert.SerializeObject(s2);
			var s1 = JsonConvert.DeserializeObject<Settings1>(serialized);
			Assert.That((int)s1.T, Is.EqualTo(2));
		}
    }
}
