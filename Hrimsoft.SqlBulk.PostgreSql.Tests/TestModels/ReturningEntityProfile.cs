using System.Data;

namespace Hrimsoft.SqlBulk.PostgreSql.Tests.TestModels
{
    /// <summary>
    /// Entity profile that defines properties that have to be included into the returning clause
    /// </summary>
    public class ReturningEntityProfile: EntityProfile
    {
        public ReturningEntityProfile(int maximumSentElements=0)
            :base(typeof(TestEntity))
        {
            this.MaximumSentElements = maximumSentElements;

            this.ToTable("simple_test_entity", "unit_tests");
            
            this.HasProperty<TestEntity, int>(entity => entity.Id)
                .ThatIsAutoGenerated()
                .HasColumnType(DbType.Int32);
            this.HasProperty<TestEntity, string>(entity => entity.RecordId)
                .MustBeUpdatedAfterInsert()
                .HasColumnType(DbType.String);
            this.HasProperty<TestEntity, string>(entity => entity.SensorId)
                .MustBeUpdatedAfterInsert()
                .HasColumnType(DbType.String);
            this.HasProperty<TestEntity, int>(entity => entity.Value)
                .HasColumnType(DbType.Int32);
        }
    }
}