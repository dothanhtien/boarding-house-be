using System.Runtime.CompilerServices;
using FluentValidation;

namespace BoardingHouse.UnitTests;

internal static class ValidatorDefaults
{
    [ModuleInitializer]
    internal static void Initialize() => ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;
}
