using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace NunMassacre.Multiplayer
{
    // Unity's native serializer omits nested types from runtime-loaded mod assemblies on this build.
    // Explicit field contracts also prevent Vector3.normalized/Quaternion properties recursing.
    internal static class JsonCodec
    {
        private sealed class FieldsOnly : DefaultContractResolver
        {
            protected override IList<JsonProperty> CreateProperties(Type type, MemberSerialization ignored)
            {
                return type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                    .Where(f => !f.IsStatic && !f.IsNotSerialized)
                    .Select(f => { var p = CreateProperty(f, MemberSerialization.Fields); p.Readable = true; p.Writable = !f.IsInitOnly; return p; }).ToList();
            }
        }
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            ContractResolver = new FieldsOnly(), TypeNameHandling = TypeNameHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore, MaxDepth = 32,
            // Keep explicit false/zero values. They are meaningful transitions for authoritative
            // world state (for example an opened door or a disabled light).
            NullValueHandling = NullValueHandling.Ignore,
            Culture = System.Globalization.CultureInfo.InvariantCulture
        };
        internal static string ToJson(object value, bool pretty = false)
        {
            return JsonConvert.SerializeObject(value, pretty ? Formatting.Indented : Formatting.None, Settings);
        }
        internal static T FromJson<T>(string json)
        {
            using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 32 })
                return JsonSerializer.Create(Settings).Deserialize<T>(reader);
        }
    }
}
