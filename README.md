# DotnetTypeHider

DotnetTypeHider is an experimental .NET IL rewriting tool that makes decompiled output harder to understand by
disguising user-defined types as compiler-generated anonymous types.

It works by:
- Renaming eligible classes to anonymous-type–like names
- Adding the `CompilerGeneratedAttribute` to those types
- Optionally hiding certain method calls by proxying them through
  compiler-generated anonymous type constructors

The result is code that remains fully valid and verifiable, but is significantly more confusing when viewed in
common .NET decompilers. 

This project is inspired by
[Washi1337/AwaitFuscator](https://github.com/Washi1337/AwaitFuscator).

## Example

### Input
![Input Program](input.png)

### Output
![Output Program](output.png)
