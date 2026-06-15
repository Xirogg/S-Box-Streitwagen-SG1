using Sandbox;

namespace Sandbox;

/// <summary>
/// Ein einzelnes Balancing-Preset für das In-Game Debug-/Playtesting-Menü.
///
/// Jedes Feld ist <c>nullable</c>: nur Werte, die du tatsächlich setzt
/// (≠ null), werden beim Aktivieren des Presets auf den Streitwagen
/// angewendet. Alles andere bleibt auf dem Default-Wert des Spielers.
/// So bestimmst du pro Preset selbst, welche Stats verändert werden.
///
/// Neue Stats hinzufügen ist trivial: ein weiteres <c>float?</c>-Property
/// anlegen und in <see cref="ApplyTo"/> die entsprechende Zeile ergänzen.
/// </summary>
public sealed class DebugStatPreset
{
	[Property] public string Name { get; set; } = "Neues Preset";

	// --- TestControlls (Antrieb / Pferd) ---
	[Property, Group( "TestControlls" )] public float? PullForce { get; set; }
	[Property, Group( "TestControlls" )] public float? BrakeForce { get; set; }
	[Property, Group( "TestControlls" )] public float? SteerTorque { get; set; }
	[Property, Group( "TestControlls" )] public float? MaxAngularSpeed { get; set; }
	[Property, Group( "TestControlls" )] public float? LateralGrip { get; set; }

	// --- ChariotPhysics (Wagen) ---
	[Property, Group( "ChariotPhysics" )] public float? MaxSpeed { get; set; }
	[Property, Group( "ChariotPhysics" )] public float? DriftForce { get; set; }
	[Property, Group( "ChariotPhysics" )] public float? ChariotAngularDamping { get; set; }
	[Property, Group( "ChariotPhysics" )] public float? ChariotLateralGrip { get; set; }

	/// <summary>
	/// Masse/Gewicht des Wagen-Rigidbodys. Wird auf <c>Body.MassOverride</c>
	/// gesetzt (der echte <c>Mass</c>-Wert ist read-only). Muss &gt; 0 sein,
	/// damit das Override greift.
	/// </summary>
	[Property, Group( "ChariotPhysics" )] public float? ChariotMass { get; set; }

	/// <summary>
	/// Wendet alle gesetzten (≠ null) Werte dieses Presets auf die beiden
	/// Komponenten des eigenen Streitwagens an. Nicht gesetzte Werte werden
	/// nicht angefasst — der Aufrufer (DebugPlaytestPanel) stellt vorher den
	/// Baseline-Zustand wieder her, damit Presets sauber sind und sich nicht
	/// gegenseitig "überlappen".
	/// </summary>
	public void ApplyTo( TestControlls controls, ChariotPhysics chariot )
	{
		if ( controls is not null )
		{
			if ( PullForce.HasValue ) controls.PullForce = PullForce.Value;
			if ( BrakeForce.HasValue ) controls.BrakeForce = BrakeForce.Value;
			if ( SteerTorque.HasValue ) controls.SteerTorque = SteerTorque.Value;
			if ( MaxAngularSpeed.HasValue ) controls.MaxAngularSpeed = MaxAngularSpeed.Value;
			if ( LateralGrip.HasValue ) controls.LateralGrip = LateralGrip.Value;
		}

		if ( chariot is not null )
		{
			if ( MaxSpeed.HasValue ) chariot.MaxSpeed = MaxSpeed.Value;
			if ( DriftForce.HasValue ) chariot.DriftForce = DriftForce.Value;
			if ( ChariotAngularDamping.HasValue ) chariot.ChariotAngularDamping = ChariotAngularDamping.Value;
			if ( ChariotLateralGrip.HasValue ) chariot.LateralGrip = ChariotLateralGrip.Value;
			if ( ChariotMass.HasValue && chariot.Body is not null ) chariot.Body.MassOverride = ChariotMass.Value;
		}
	}

	/// <summary>
	/// Liest die aktuellen Werte beider Komponenten in dieses Preset ein.
	/// Wird genutzt, um beim Start einen "Baseline"-Snapshot der Original-
	/// Werte zu erzeugen, zu dem zwischen den Presets zurückgesetzt wird.
	/// </summary>
	public static DebugStatPreset Capture( string name, TestControlls controls, ChariotPhysics chariot )
	{
		var p = new DebugStatPreset { Name = name };
		if ( controls is not null )
		{
			p.PullForce = controls.PullForce;
			p.BrakeForce = controls.BrakeForce;
			p.SteerTorque = controls.SteerTorque;
			p.MaxAngularSpeed = controls.MaxAngularSpeed;
			p.LateralGrip = controls.LateralGrip;
		}
		if ( chariot is not null )
		{
			p.MaxSpeed = chariot.MaxSpeed;
			p.DriftForce = chariot.DriftForce;
			p.ChariotAngularDamping = chariot.ChariotAngularDamping;
			p.ChariotLateralGrip = chariot.LateralGrip;
			if ( chariot.Body is not null )
				p.ChariotMass = chariot.Body.MassOverride;
		}
		return p;
	}
}
