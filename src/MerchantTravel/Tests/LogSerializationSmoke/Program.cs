using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Inspect and instantiate the actual compiler-generated native-commit log payload.
// Loading this anonymous type invokes no Unity component, game code or save access.
if (args.Length != 1) throw new ArgumentException("Pass the freshly built MerchantTravel DLL.");
string path = Path.GetFullPath(args[0]);
using var stream = File.OpenRead(path);
using var pe = new PEReader(stream);
MetadataReader metadata = pe.GetMetadataReader();
var expected = new HashSet<string> { "target", "history", "destinationRestored", "originalDestinationId", "temporaryDestinationId", "nativeCommitCount" };
string selectedName = null;
int selectedTypeRow = 0;
foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
{
    TypeDefinition type = metadata.GetTypeDefinition(handle);
    var properties = type.GetProperties().Select(p => metadata.GetString(metadata.GetPropertyDefinition(p).Name)).ToHashSet();
    if (!properties.SetEquals(expected)) continue;
    if (selectedName != null) throw new Exception("Ambiguous compiled commit-log contract.");
    selectedName = metadata.GetString(type.Name);
    selectedTypeRow = MetadataTokens.GetRowNumber(handle);
}
if (selectedName == null) throw new Exception("Compiled native commit payload missing.");
bool scalarInstantiationFound = false;
for (int row = 1; row <= metadata.GetTableRowCount(TableIndex.TypeSpec); row++)
{
    TypeSpecification specification = metadata.GetTypeSpecification(MetadataTokens.TypeSpecificationHandle(row));
    BlobReader signature = metadata.GetBlobReader(specification.Signature);
    if (signature.RemainingBytes < 4 || signature.ReadByte() != 0x15 || signature.ReadByte() != 0x12) continue;
    if (signature.ReadCompressedInteger() != selectedTypeRow * 4) continue;
    if (signature.ReadCompressedInteger() != 6) throw new Exception("Unexpected compiled log arity.");
    // ECMA-335: string, single, bool, int32, int32, int32. A Vector3 type argument fails here.
    byte[] actual = signature.ReadBytes(signature.RemainingBytes);
    // The generated class's own Equals/GetHashCode also refers to its open !0..!5 parameters.
    if (actual.SequenceEqual(new byte[] { 0x13, 0, 0x13, 1, 0x13, 2, 0x13, 3, 0x13, 4, 0x13, 5 })) continue;
    if (!actual.SequenceEqual(new byte[] { 0x0e, 0x0c, 0x02, 0x08, 0x08, 0x08 }))
        throw new Exception("Actual compiled commit log contains a non-scalar type.");
    scalarInstantiationFound = true;
}
if (!scalarInstantiationFound) throw new Exception("Compiled scalar log instantiation missing.");
Type openType = Assembly.LoadFrom(path).GetType(selectedName, true);
var values = new Dictionary<string, object> { ["target"] = "dock.the-iron-rig", ["history"] = 165.5f,
    ["destinationRestored"] = true, ["originalDestinationId"] = 101, ["temporaryDestinationId"] = 202, ["nativeCommitCount"] = 1 };
string[] names = openType.GetConstructors().Single().GetParameters().Select(p => p.Name).ToArray();
Type closedType = openType.MakeGenericType(names.Select(n => values[n].GetType()).ToArray());
object payload = Activator.CreateInstance(closedType, names.Select(n => values[n]).ToArray());
string json = JsonConvert.SerializeObject(payload);
JObject result = JObject.Parse(json);
if (result["destinationRestored"].Type != JTokenType.Boolean || !(bool)result["destinationRestored"] ||
    (int)result["originalDestinationId"] != 101 || (int)result["temporaryDestinationId"] != 202 ||
    (int)result["nativeCommitCount"] != 1 || json.Contains("normalized")) throw new Exception("Unsafe commit log contract.");
Console.WriteLine("PASS actual compiled NATIVE_COMMIT_RETURN payload serializes to scalar JSON without Unity vector recursion.");
Console.WriteLine(json);
