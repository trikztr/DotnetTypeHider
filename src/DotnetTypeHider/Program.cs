using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.DotNet.Signatures.Types;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;

var module = ModuleDefinition.FromFile(args[0]);
HideTypes(module);
HideCalls(module);
module.Write("out.dll");
return;

static void HideTypes(ModuleDefinition module)
{
    foreach (var type in module.GetAllTypes())
    {
        if (
            type.IsValueType
            || !type.IsClass
            || type.IsModuleType
            || type.IsCompilerGenerated()
            || type == module.ManagedEntryPointMethod?.DeclaringType
        )
            continue;

        type.CustomAttributes.Add(new CustomAttribute(GetCompilerGeneratedAttribute(module)));
        type.Name = RandomAnonymousTypeName();
    }
}

static void HideCalls(ModuleDefinition module)
{
    Dictionary<IMethodDescriptor, IMethodDefOrRef> cache = new(ReferenceEqualityComparer.Instance);
    foreach (var method in module.GetAllTypes().SelectMany(type => type.Methods))
    {
        if (
            method
            is not { Signature: { }, CilMethodBody.Instructions: { Count: > 1 } instructions }
        )
            continue;

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
            )
                continue;

            if (!cache.TryGetValue(targetDescriptor, out var anonymousTypeConstructor))
            {
                anonymousTypeConstructor = cache[targetDescriptor] =
                    module.DefaultImporter.ImportMethod(
                        HideCallInAnonymousTypeConstructor(module, targetDescriptor)
                    );
            }

            // Replace call with newobj
            instruction.OpCode = CilOpCodes.Newobj;
            instruction.Operand = anonymousTypeConstructor;

            // Discard created object
            instructions.Insert(i + 1, CilOpCodes.Pop);
            i++;
        }
    }
}

static (TypeDefinition AnonymousObject, MethodDefinition Constructor) CreateAnonymousObjectType(
    ModuleDefinition module,
    TypeSignature[] parameterTypes
)
{
    var anonymousType = new TypeDefinition(
        "",
        RandomAnonymousTypeName(),
        TypeAttributes.Class | TypeAttributes.NotPublic | TypeAttributes.Sealed,
        module.CorLibTypeFactory.Object.Type
    );
    anonymousType.CustomAttributes.Add(new CustomAttribute(GetCompilerGeneratedAttribute(module)));

    var constructorSignature = MethodSignature.CreateInstance(
        module.CorLibTypeFactory.Void,
        parameterTypes
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
    var objectConstructor = module.CorLibTypeFactory.Object.Type.Resolve()!.GetConstructor()!;

    instructions.Add(CilOpCodes.Ldarg_0);
    instructions.Add(CilOpCodes.Call, module.DefaultImporter.ImportMethod(objectConstructor));
    instructions.Add(CilOpCodes.Ret);
    instructions.CalculateOffsets();
    body.ComputeMaxStack();

    constructor.CilMethodBody = body;
    anonymousType.Methods.Add(constructor);
    module.TopLevelTypes.Add(anonymousType);

    return (anonymousType, constructor);
}

static MethodDefinition HideCallInAnonymousTypeConstructor(
    ModuleDefinition module,
    IMethodDescriptor target
)
{
    var (anonymousType, constructor) = CreateAnonymousObjectType(
        module,
        [.. target.Signature!.ParameterTypes]
    );

    var body = constructor.CilMethodBody!;
    var instructions = body.Instructions;

    // forward arguments
    for (int i = 0; i < constructor.Parameters.Count; i++)
    {
        instructions.Insert(instructions.Count - 1, CilOpCodes.Ldarg, constructor.Parameters[i]);
    }

    instructions.Insert(
        instructions.Count - 1,
        target.Signature.HasThis ? CilOpCodes.Callvirt : CilOpCodes.Call,
        module.DefaultImporter.ImportMethod(target)
    );

    instructions.CalculateOffsets();
    body.ComputeMaxStack();

    return constructor;
}

static string RandomAnonymousTypeName() => $"<>AnonType_{Guid.NewGuid():N}";

static MemberReference GetCompilerGeneratedAttribute(ModuleDefinition module) =>
    module
        .CorLibTypeFactory.CorLibScope.CreateTypeReference(
            "System.Runtime.CompilerServices",
            "CompilerGeneratedAttribute"
        )
        .CreateMemberReference(
            ".ctor",
            MethodSignature.CreateInstance(module.CorLibTypeFactory.Void)
        )
        .ImportWith(module.DefaultImporter);
