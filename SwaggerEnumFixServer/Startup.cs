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

            IEnumerable<JToken> enums = GetEnums(swaggerRoot).ToList();
            ModifyEnums(enums);

            FixDuplicateOperations(swaggerRoot);

            byte[] responseBytes = Encoding.UTF8.GetBytes(swaggerRoot.ToString(Formatting.Indented));
            var responseStream = (Stream)environment["owin.ResponseBody"];
            var responseHeaders = (IDictionary<string, string[]>)environment["owin.ResponseHeaders"];

            responseHeaders["Content-Length"] = new string[] { responseBytes.Length.ToString(CultureInfo.InvariantCulture) };
            responseHeaders["Content-Type"] = new string[] { "application/json" };


            await responseStream.WriteAsync(responseBytes, 0, responseBytes.Length);
        }

        private void FixDuplicateOperations(JObject swaggerRoot)
        {
            swaggerRoot
        }

        public readonly Dictionary<string, string> EnumNameMapping = new Dictionary<string, string>()
        {
            {"GetFeatures", "GroupFeature"}
        };

        private void ModifyEnums(IEnumerable<JToken> enums)
        {
            foreach (var enumProperty in enums)
            {
                JContainer groupNode = enumProperty.Parent.Parent;
                
                bool isResponseType = enumProperty.Path.StartsWith("paths");

                string enumName;
                if (isResponseType)
                {
                    // man man man, als dit ooit netjes moet, terug naar rootnode en met jsonpath zoeken
                    JObject operationIdProperty = (JObject)enumProperty.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent.Parent;

                    string operationIdName = operationIdProperty.Value<string>("operationId");

                    bool hasMapping = EnumNameMapping.TryGetValue(operationIdName, out enumName);

                    if (!hasMapping)
                    {
                        enumName = operationIdName;
                    }
                }
                else
                {
                    string typeName = ((JProperty) groupNode.Parent).Name;
                    enumName = typeName.Substring(0, 1).ToUpper() + typeName.Substring(1);
                }


                groupNode.Add(
                    new JProperty("x-ms-enum", new JObject
                    {
                        new JProperty("name", enumName),
                        new JProperty("modelAsString", false)

                    }));
            }
        }

        private IEnumerable<JToken> GetEnums(JToken token)
        {
            // find enum
            if (token is JProperty property && property.Name == "enum")
            {
                return property;
            }

            return GetEnums(token.Children()).ToList();
        }

        private IEnumerable<JToken> GetEnums(JEnumerable<JToken> jTokenCollection)
        {
            return jTokenCollection.SelectMany(GetEnums);
        }
    }
}
