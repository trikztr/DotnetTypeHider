using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;

var module = ModuleDefinition.FromFile(args[0]);
var factory = module.CorLibTypeFactory;

// CompilerGeneratedAttribute ctor
var compilerGeneratedAttribute = factory
    .CorLibScope.CreateTypeReference(
        "System.Runtime.CompilerServices",
        "CompilerGeneratedAttribute"
    )
    .CreateMemberReference(".ctor", MethodSignature.CreateInstance(factory.Void))
    .ImportWith(module.DefaultImporter);

// Rename + mark types
foreach (var type in module.GetAllTypes())
{
    if (type.IsValueType || !type.IsClass || type.IsModuleType || type.IsCompilerGenerated())
        continue;

    type.CustomAttributes.Add(new CustomAttribute(compilerGeneratedAttribute));
    type.Name = $"<>AnonType_{type.MetadataToken}";
}

// Hide calls in anonymous types constructors
foreach (var type in module.GetAllTypes())
{
    foreach (var method in type.Methods)
    {
        if (method.CilMethodBody is not { } body)
            continue;

        var instructions = body.Instructions;

        for (int i = 0; i < instructions.Count; i++)
        {
            var instruction = instructions[i];

            if (
                (instruction.OpCode != CilOpCodes.Call && instruction.OpCode != CilOpCodes.Callvirt)
                || instruction.Operand
                    is not IMethodDescriptor
                    {
                        Signature: { ReturnsValue: false, HasThis: false }
                    } targetDescriptor
                || targetDescriptor.Resolve() is not { } target
            )
                continue;

            var anonymousType = CreateAnonymousTypeForCall(module, target);
            var anonymousTypeConstructor = anonymousType.Methods.Single(m => m.Name == ".ctor");

            // Replace call with newobj
            instruction.OpCode = CilOpCodes.Newobj;
            instruction.Operand = module.DefaultImporter.ImportMethod(anonymousTypeConstructor);

            // Discard created object
            instructions.Insert(i + 1, CilOpCodes.Pop);
            i++;
        }
    }
}

module.Write("out.dll");
return;

TypeDefinition CreateAnonymousTypeForCall(ModuleDefinition module, MethodDefinition target)
{
    var anonymousType = new TypeDefinition(
        "",
        $"<>AnonType_{Guid.NewGuid():N}",
        TypeAttributes.Class | TypeAttributes.NotPublic | TypeAttributes.Sealed,
        factory.Object.Type
    );
    anonymousType.CustomAttributes.Add(new CustomAttribute(compilerGeneratedAttribute));

    var constructorSignature = MethodSignature.CreateInstance(
        factory.Void,
        [.. target.Signature!.ParameterTypes]
    );
    var constructor = new MethodDefinition(
        ".ctor",
        MethodAttributes.Public
            | MethodAttributes.HideBySig
            | MethodAttributes.SpecialName
            | MethodAttributes.RuntimeSpecialName,
        constructorSignature
    );

    var body = new CilMethodBody(constructor);
    var instructions = body.Instructions;

    // object::.ctor
    var objectConstructor = factory.Object.Type.Resolve()!.GetConstructor()!;

    instructions.Add(CilOpCodes.Ldarg_0);
    instructions.Add(CilOpCodes.Call, module.DefaultImporter.ImportMethod(objectConstructor));

    // forward arguments
    for (int i = 0; i < constructor.Parameters.Count; i++)
    {
        instructions.Add(CilOpCodes.Ldarg, constructor.Parameters[i]);
    }

    instructions.Add(
        target.IsVirtual ? CilOpCodes.Callvirt : CilOpCodes.Call,
        module.DefaultImporter.ImportMethod(target)
    );

    instructions.Add(CilOpCodes.Ret);
    instructions.CalculateOffsets();

    constructor.CilMethodBody = body;
    anonymousType.Methods.Add(constructor);
    module.TopLevelTypes.Add(anonymousType);

    return anonymousType;
}
