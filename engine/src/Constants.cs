//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("noz")]
[assembly: InternalsVisibleTo("NoZ.Editor")]

namespace NoZ;

internal static class Constants
{
    public const uint AssetSignature = 0x4E4F5A41; // "NOZA"
}
