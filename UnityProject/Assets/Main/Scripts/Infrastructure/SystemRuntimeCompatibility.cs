// C# 9 `init` support polyfill for Unity 2022.3 (Mono/IL2CPP do not ship
// System.Runtime.CompilerServices.IsExternalInit). Keep this file tiny and
// internal — it only exists so init-only properties compile.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
