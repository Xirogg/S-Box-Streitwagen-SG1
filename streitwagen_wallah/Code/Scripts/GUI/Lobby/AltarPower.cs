using Sandbox;

namespace Sandbox;

/// <summary>
/// Eine Gottheit / Fähigkeit auf dem Opferaltar: Anzeigename + Icon, das in den
/// runden Kreis der Säule gelegt wird. Wird als Liste im Inspector der
/// <c>AltarGUI</c>-Component editiert (Name eingeben, Icon-PNG reinziehen).
/// </summary>
public sealed class AltarPower
{
	[Property] public string Name { get; set; } = "Gott";

	/// <summary>God-Power-Icon (z.B. Maat_Altar.png). Liegt mittig im Säulenkreis.</summary>
	[Property] public Texture Icon { get; set; }
}
