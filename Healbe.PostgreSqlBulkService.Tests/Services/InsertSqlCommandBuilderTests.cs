using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading;
using Healbe.PostgreSqlBulkService.Tests.TestModels;
using Hrimsoft.PostgresSqlBulkService;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Healbe.PostgreSqlBulkService.Tests.Services
{
    public class InsertSqlCommandBuilderTests
    {
        private InsertSqlCommandBuilder _testService;
        
        [SetUp]
        public void SetUp()
        {
            _testService = new InsertSqlCommandBuilder(NullLoggerFactory.Instance);
        }

        [Test]
        public void Should_exclude_autogenerated_columns()
        {
            var entityProfile = new SimpleEntityProfile();
            var elements = new List<TestEntity>
            {
                new TestEntity {RecordId = "rec-01"}
            };
            var (command, parameters) = _testService.Generate(elements, entityProfile, CancellationToken.None);
            Assert.NotNull(command);
            
            Assert.IsFalse(command.StartsWith("insert into \"test_entity\" (\"id\""));
        }
        
        [Test]
        public void Should_include_autogenerated_column_into_returning_clause()
        {
            var entityProfile = new SimpleEntityProfile();
            var elements = new List<TestEntity>
            {
                new TestEntity {RecordId = "rec-01"}
            };
            var (command, parameters) = _testService.Generate(elements, entityProfile, CancellationToken.None);
            Assert.NotNull(command);
            
            Assert.IsTrue(command.EndsWith("returning \"id\";"));
        }
        
        [Test]
        public void Should_close_command_with_semicolon()
        {
            var entityProfile = new SimpleEntityProfile();
            var elements = new List<TestEntity>
            {
                new TestEntity {RecordId = "rec-01"}
            };
            var (command, parameters) = _testService.Generate(elements, entityProfile, CancellationToken.None);
            Assert.NotNull(command);
            
            Assert.IsTrue(command.EndsWith(";"));
        }
        
        [Test]
        public void Should_return_correct_parameters_values()
        {
            var entityProfile = new SimpleEntityProfile();
            var elements = new List<TestEntity>
            {
                new TestEntity {RecordId = "rec-01", SensorId = "sens-02", Value = 12}
            };
            var (command, parameters) = _testService.Generate(elements, entityProfile, CancellationToken.None);
            Assert.NotNull(command);
            Assert.NotNull(parameters);
            
            Assert.AreEqual(3, parameters.Count);
            Assert.NotNull(parameters.FirstOrDefault(p => p.Value.ToString() == "rec-01"));
            Assert.NotNull(parameters.FirstOrDefault(p => p.Value.ToString() == "sens-02"));
            Assert.NotNull(parameters.FirstOrDefault(p => p.DbType == DbType.Int32 && (int)p.Value == 12));
        }
        [Test]
        public void Should_return_correct_parameters_types()
        {
            var entityProfile = new SimpleEntityProfile();
            var elements = new List<TestEntity>
            {
                new TestEntity {RecordId = "rec-01", SensorId = "sens-02", Value = 12}
            };
            var (command, parameters) = _testService.Generate(elements, entityProfile, CancellationToken.None);
            Assert.NotNull(command);
            Assert.NotNull(parameters);
            
            Assert.AreEqual(3, parameters.Count);
            Assert.AreEqual(2, parameters.Where(p => p.DbType == DbType.String).ToList().Count);
            Assert.NotNull(parameters.FirstOrDefault(p => p.DbType == DbType.Int32));
        }
        
        [Test]
        public void Should_include_table_name()
        {
            var entityProfile = new SimpleEntityProfile();
            var elements = new List<TestEntity>
            {
                new TestEntity {RecordId = "rec-01"}
            };
            var (command, parameters) = _testService.Generate(elements, entityProfile, CancellationToken.None);
            Assert.NotNull(command);
            
            Assert.IsTrue(command.ToLowerInvariant().StartsWith("insert into \"test_entity\""));
        }
        
        [Test]
        public void Should_include_scheme_and_table_name()
        {
            var entityProfile = new EntityProfile(typeof(TestEntity));
            entityProfile.HasProperty<TestEntity, int>(x => x.Id);
            entityProfile.ToTable("test_entity", "custom");
            var elements = new List<TestEntity>
            {
                new TestEntity {RecordId = "rec-01"}
            };
            var (command, parameters) = _testService.Generate(elements, entityProfile, CancellationToken.None);
            Assert.NotNull(command);
            
            Assert.IsTrue(command.ToLowerInvariant().StartsWith("insert into \"custom\".\"test_entity\""));
        }
    }
}