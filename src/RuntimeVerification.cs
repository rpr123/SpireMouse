namespace SpireMouse;

internal static partial class RuntimeVerification
{
#if !VERIFY
    internal static string? DirectoryPath => null;
    internal static void IsolateSavesIfRequested() { }
#endif
}
