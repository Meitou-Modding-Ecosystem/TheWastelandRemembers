using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: BuildStamp <assembly.dll> <mod class name>");
    return 2;
}

try
{
    using var stream = File.OpenRead(args[0]);
    using var pe = new PEReader(stream);
    var metadata = pe.GetMetadataReader();
    foreach (var handle in metadata.TypeDefinitions)
    {
        var type = metadata.GetTypeDefinition(handle);
        if (metadata.GetString(type.Namespace) != "LivingWasteland.MeitouMod" || metadata.GetString(type.Name) != args[1]) continue;
        foreach (var fieldHandle in type.GetFields())
        {
            var field = metadata.GetFieldDefinition(fieldHandle);
            if (metadata.GetString(field.Name) != "HelperBuild") continue;
            var value = field.GetDefaultValue();
            if (value.IsNil || metadata.GetConstant(value) is not { TypeCode: ConstantTypeCode.String } constant)
                throw new InvalidDataException("HelperBuild is not a string constant.");
            var bytes = metadata.GetBlobReader(constant.Value);
            Console.WriteLine("HelperBuild=" + bytes.ReadUTF16(bytes.Length));
            return 0;
        }
        Console.WriteLine("HelperBuild=");
        return 0;
    }
    throw new InvalidDataException($"Mod entry class {args[1]} was not found.");
}
catch (Exception error) when (error is IOException or InvalidDataException or BadImageFormatException or InvalidOperationException or UnauthorizedAccessException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
