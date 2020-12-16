using System;
using System.Collections.Generic;
using System.Json;
using Topos.Config;
using OpenFTTH.SearchIndexer.Config;
using OpenFTTH.SearchIndexer.Serialization;
using Typesense;
using OpenFTTH.SearchIndexer.Model;
using Microsoft.Extensions.Logging;
using OpenFTTH.SearchIndexer.Database;
using System.Timers;
using System.Threading.Tasks;

namespace OpenFTTH.SearchIndexer.Consumer
{
    public class AddressConsumer : IAddressConsumer
    {
        private IDisposable _consumer;
        private List<IDisposable> _consumers = new List<IDisposable>();
        private ITypesenseClient _client;

        private readonly KafkaSetting _kafkaSetting;
        private readonly DatabaseSetting _databaseSetting;

        private ILogger<AddressConsumer> _logger;
        private readonly IPostgresWriter _postgresWriter;

        private Dictionary<string, List<JsonObject>> _topicList = new Dictionary<string, List<JsonObject>>();
        private bool _isBulkFinished;
        private DateTime _lastMessageReceivedBulk;
        private Timer _bulkInsertTimer;

        public AddressConsumer(ITypesenseClient client, ILogger<AddressConsumer> logger, IPostgresWriter postgresWriter)
        {
            _client = client;
            _logger = logger;
            _postgresWriter = postgresWriter;
        }

        public void Subscribe()
        {
            var adresseList = new List<Address>();
            var schema = new Schema
            {
                Name = "Addresses",
                Fields = new List<Field>
                {
                    new Field("id_lokalId", "string", false),
                    new Field("door", "string", false),
                    new Field("doorPoint", "string", false),
                    new Field("floor", "string", false),
                    new Field("unitAddressDescription", "string", false),
                    new Field("houseNumberId", "string", false),
                    new Field("houseNumberDirection", "string", false),
                    new Field("houseNumberText", "string", false),
                    new Field("position", "string", false),
                    new Field("accessAddressDescription", "string", false),
                    new Field("status","int32",false)
                },
                DefaultSortingField = "status"
            };
            var list = new List<JsonObject>();
            //Consume();
        }

        private void CheckBulkStatus(object source, ElapsedEventArgs e)
        {
            var elapsedTime = DateTime.UtcNow - _lastMessageReceivedBulk;

            _logger.LogInformation($"Elapsed time: {elapsedTime.TotalSeconds}");

            if (elapsedTime.TotalSeconds > 10)
            {
                _isBulkFinished = true;
            }
        }

        public void SubscribeBulk()
        {
            var kafka = new KafkaSetting();
            kafka.DatafordelereTopic = "DAR";
            kafka.Server = "localhost:9092";
            kafka.PositionFilePath = "/tmp/";

            var database = new DatabaseSetting();
            database.ConnectionString = "Host=172.18.0.5;Username=postgres;Password=postgres;Database=gis";
            Dictionary<string, string> adress = new Dictionary<string, string>{
                {"AdresseList","id_lokalId,door,doorPoint,floor,unitAddressDescription,houseNumber"},
                {"HusnummerList","id_lokalId,status,houseNumberText,houseNumberDirection,accessAddressDescription,position"}
            };
            database.Values = adress;

            var AdresseList = new List<JsonValue>();
            var hussnumerList = new List<JsonValue>();

            _bulkInsertTimer = new Timer();
            _bulkInsertTimer.Elapsed += new ElapsedEventHandler(CheckBulkStatus);
            _bulkInsertTimer.Interval = 1000;

            _bulkInsertTimer.Start();
            var consumer = _consumer = Configure
                      .Consumer(kafka.DatafordelereTopic, c => c.UseKafka(kafka.Server))
                      .Serialization(s => s.DatafordelerEventDeserializer())
                      .Topics(t => t.Subscribe(kafka.DatafordelereTopic))
                      .Positions(p => p.StoreInFileSystem(kafka.PositionFilePath))
                      .Handle(async (messages, context, token) =>
                      {
                          foreach (var message in messages)
                          {
                              _lastMessageReceivedBulk = DateTime.UtcNow;
                              if (message.Body is JsonObject)
                              {

                                  if (!_topicList.ContainsKey(kafka.DatafordelereTopic))
                                  {
                                      _topicList.Add(kafka.DatafordelereTopic, new List<JsonObject>());
                                      _topicList[kafka.DatafordelereTopic].Add((JsonObject)message.Body);
                                  }
                                  else
                                  {

                                      _topicList[kafka.DatafordelereTopic].Add((JsonObject)message.Body);
                                  }

                                  if (_topicList[kafka.DatafordelereTopic].Count >= 10000)
                                  {

                                      foreach (var obj in database.Values)
                                      {

                                          var tableName = obj.Key;

                                          var columns = obj.Value.Split(",");

                                          var batch = CheckObjectType(_topicList[kafka.DatafordelereTopic], tableName);
                                          if (batch != null)
                                          {
                                              _postgresWriter.AddToPSQL(batch, tableName, columns, database.ConnectionString);
                                          }
                                      }
                                      _topicList[kafka.DatafordelereTopic].Clear();
                                  }
                              }
                          }
                      }).Start();
        }

        public bool IsBulkFinished()
        {
            return _isBulkFinished;
        }

        public void ProcessDataTypesense()
        {
            var typsenseItems = _postgresWriter.JoinTables("houseNumber","id_lokalId","Host=172.18.0.5;Username=postgres;Password=postgres;Database=gis");
            
        }

        public Address ConvertIntoAdress(JsonValue obj)
        {
            var address = new Address
            {
                id_lokalId = (string)obj["id_lokalId"],
                door = (string)obj["door"],
                doorPoint = (string)obj["doorPoint"],
                floor = (string)obj["floor"],
                unitAddressDescription = (string)obj["unitAddressDescription"],
                houseNumberId = (string)obj["houseNumber"],
                houseNumberDirection = (string)obj["houseNumberDirection"],
                houseNumberText = (string)obj["houseNumberText"],
                position = (string)obj["position"],
                accessAddressDescription = (string)obj["accessAddressDescription"],
                status = (int)obj["status"]
            };

            return address;
        }

        private List<JsonObject> CheckObjectType(List<JsonObject> items, string tableName)
        {
            var batch = new List<JsonObject>();

            foreach (var item in items)
            {
                if (item["type"] == tableName)
                {
                    batch.Add(item);
                }
            }

            return batch;
        }

        private void Consume()
        {
            var kafka = new KafkaSetting();
            kafka.DatafordelereTopic = "DAR";
            kafka.Server = "localhost:9092";
            kafka.PositionFilePath = "/tmp/";

            var AdresseList = new List<JsonValue>();
            var hussnumerList = new List<JsonValue>();

            DateTime waitStartTimestamp = DateTime.UtcNow;

            var consumer = _consumer = Configure
                      .Consumer(kafka.DatafordelereTopic, c => c.UseKafka(kafka.Server))
                      .Serialization(s => s.DatafordelerEventDeserializer())
                      .Topics(t => t.Subscribe(kafka.DatafordelereTopic))
                      .Positions(p => p.StoreInFileSystem(kafka.PositionFilePath))
                      .Handle(async (messages, context, token) =>
                      {

                          foreach (var message in messages)
                          {
                              if (message.Body is JsonObject)
                              {
                                  var messageTime = DateTime.UtcNow;

                                  TimeSpan timespan = waitStartTimestamp - messageTime;

                                  if (timespan.TotalSeconds > 10)
                                  {
                                      _logger.LogInformation("It worked");
                                  }
                              }
                          }
                      }).Start();

            _logger.LogInformation("It got here");
        }


        public void Dispose()
        {
            _consumers.ForEach(x => x.Dispose());
            if (!(_bulkInsertTimer is null))
            {
                _bulkInsertTimer.Stop();
                _bulkInsertTimer.Dispose();
            }
        }
    }
}
