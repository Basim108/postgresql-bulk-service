using System.Data;
using Hrimsoft.PostgresSqlBulkService;

namespace Healbe.PostgreSqlBulkService.Tests.TestModels
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

            this.HasProperty<TestEntity, int>(entity => entity.Id)
                .ThatIsAutoGeneratedPrivateKey()
                .HasColumnType(DbType.Int32);
            this.HasProperty<TestEntity, string>(entity => entity.RecordId)
                .ThatIsPartOfKey()
                .HasColumnType(DbType.String);
            this.HasProperty<TestEntity, string>(entity => entity.SensorId)
                .ThatIsPartOfKey()
                .HasColumnType(DbType.String);
            this.HasProperty<TestEntity, int>(entity => entity.Value)
                .HasColumnType(DbType.Int32);
        }
    }
}