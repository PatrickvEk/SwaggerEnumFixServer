using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SwaggerEnumFixServer
{
    public class Startup
    {
        public void ConfigureServices(IServiceCollection services)
        {
        }

        public void Configure(IApplicationBuilder app)
        {
            app.UseOwin(pipeline =>
            {
                pipeline(next => ProcessSwagger);
            });
        }

        public async Task ProcessSwagger(IDictionary<string, object> environment)
        {

            string swaggerJsonUrl = @"https://voipservice-test.routit.com/swagger/v1/swagger.json";

            HttpClient httpClient = new HttpClient();
            string jsonContent = await httpClient.GetStringAsync(swaggerJsonUrl);

            JObject swaggerRoot = JObject.Parse(jsonContent);

            FixEnums(swaggerRoot);
            FixOperationIds(swaggerRoot);


            byte[] responseBytes = Encoding.UTF8.GetBytes(swaggerRoot.ToString(Formatting.Indented));
            var responseStream = (Stream)environment["owin.ResponseBody"];
            var responseHeaders = (IDictionary<string, string[]>)environment["owin.ResponseHeaders"];

            responseHeaders["Content-Length"] = new string[] { responseBytes.Length.ToString(CultureInfo.InvariantCulture) };
            responseHeaders["Content-Type"] = new string[] { "application/json" };


            await responseStream.WriteAsync(responseBytes, 0, responseBytes.Length);
        }

        public readonly Dictionary<string, string> EnumNameMapping = new Dictionary<string, string>()
        {
            {"GetFeatures", "GroupFeature"}
        };

        public readonly Dictionary<string, string> OperationIdMapping = new Dictionary<string, string>()
        {
            {"GetFeatures", "GroupFeature"}
        };


        private void FixOperationIds(JObject swaggerRoot)
        {
            IList<JToken> operations = GetOperations(swaggerRoot);
            ModifyOperationIds(operations);
        }

        private void ModifyOperationIds(IList<JToken> operations)
        {
            // https://stackoverflow.com/questions/18547354/c-sharp-linq-find-duplicates-in-list
            var duplicateCollection = operations.GroupBy(x => x.First.ToString()).Where(g => g.Count() > 1).Select(y => y).ToList();

            foreach (IGrouping<string, JToken> duplicate in duplicateCollection)
            {
                foreach (JToken duplicateElement in duplicate)
                {
                    findAlternativeForDuplicateElement(duplicateElement);
                }
            }
        }

        private void findAlternativeForDuplicateElement(JToken duplicateElement)
        {
            JObject parent = (JObject)duplicateElement.Parent;
            string firstTag = parent.SelectToken("tags").First.Value<string>();

            JValue operationIdNode = (JValue) duplicateElement.First;
            object currentOperationId = operationIdNode.Value;

            string newOperationId = currentOperationId + firstTag;

            operationIdNode.Value = newOperationId;
        }

        private void FixEnums(JObject swaggerRoot)
        {
            IList<JToken> enums = GetEnums(swaggerRoot);
            ModifyEnums(enums);
        }


        private void ModifyEnums(IList<JToken> enums)
        {
            foreach (var enumProperty in enums)
            {
                JContainer groupNode = enumProperty.Parent;

                bool isResponseType = enumProperty.Path.StartsWith("paths");

                string enumName;
                if (isResponseType)
                {
                    // man man man, als dit ooit netjes moet, terug naar rootnode en met jsonpath zoeken
                    JObject operationIdProperty = (JObject)enumProperty.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent;

                    string operationIdName = operationIdProperty.Value<string>("operationId");

                    bool hasMapping = EnumNameMapping.TryGetValue(operationIdName, out enumName);

                    if (!hasMapping)
                    {
                        enumName = operationIdName;
                    }
                }
                else
                {
                    string typeName = ((JProperty)groupNode.Parent).Name;
                    enumName = typeName.Substring(0, 1).ToUpper() + typeName.Substring(1);
                }


                try
                {
                    groupNode.Add(
                        new JProperty("x-ms-enum", new JObject
                        {
                            new JProperty("name", enumName),
                            new JProperty("modelAsString", false)

                        }));
                }
                catch (Exception)
                {
                    // ignore on error like 'already exists'
                }
            }
        }

        private IList<JToken> GetEnums(JToken token)
        {
            bool IsEnum(JToken jToken) => jToken is JProperty property && property.Name == "enum";

            return GetNodes(token, IsEnum);
        }

        private IList<JToken> GetOperations(JToken token)
        {
            bool IsEnum(JToken jToken) => jToken is JProperty property && property.Name == "operationId";

            return GetNodes(token, IsEnum);
        }


        private IList<JToken> GetNodes(JToken token, Func<JToken, bool> predicate)
        {
            if (predicate.Invoke(token))
            {
                return new[] { token };
            }

            return GetNodes(token.Children(), predicate).ToList();
        }


        private IList<JToken> GetNodes(JEnumerable<JToken> jTokenCollection, Func<JToken, bool> predicate)
        {
            return jTokenCollection.SelectMany(node => GetNodes(node, predicate)).ToList();
        }
    }
}
