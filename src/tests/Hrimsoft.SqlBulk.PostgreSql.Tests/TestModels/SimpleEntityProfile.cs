using System.Data;

namespace Hrimsoft.SqlBulk.PostgreSql.Tests.TestModels
{
    public class SimpleEntityProfile: EntityProfile
    {
        public SimpleEntityProfile(int maximumSentElements=0)
            :base(typeof(TestEntity))
        {
            this.MaximumSentElements = maximumSentElements;

            this.ToTable("simple_test_entity", "unit_tests");

            this.HasProperty<TestEntity, int>(entity => entity.Id)
                .ThatIsAutoGenerated()
                .ThatIsPrivateKey();
            this.HasProperty<TestEntity, string>(entity => entity.RecordId);
            this.HasProperty<TestEntity, string>(entity => entity.SensorId);
            this.HasProperty<TestEntity, int>(entity => entity.Value);
        }
    }
}