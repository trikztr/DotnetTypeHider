// See https://aka.ms/new-console-template for more information
using AsmResolver.DotNet;
using AsmResolver.DotNet.Signatures;

Console.WriteLine("Hello, World!");

var module = ModuleDefinition.FromFile(args[0]);
var factory = module.CorLibTypeFactory;
var compilerGeneratedAttribute = factory
    .CorLibScope.CreateTypeReference(
        "System.Runtime.CompilerServices",
        "CompilerGeneratedAttribute"
    )
    .CreateMemberReference(".ctor", MethodSignature.CreateInstance(factory.Void))
    .ImportWith(module.DefaultImporter);
foreach (var type in module.GetAllTypes())
{
    if (!type.IsClass || type.IsModuleType || type.IsCompilerGenerated())
    {
        continue;
    }
    type.CustomAttributes.Add(new CustomAttribute(compilerGeneratedAttribute));
    type.Name = $"<>AnonType_{type.MetadataToken}";
}
module.Write("out.dll");
