using Sandbox;

/// <summary>
/// Host-autoritativer Kauf der Wagen-Skins ("Nasen"/Köpfe). Nur der Krokodilkopf kostet etwas
/// (<see cref="KrokodilkopfPrice"/> PG); der Pferdekopf ist der Basis-Skin und gehört jedem sofort
/// (siehe <see cref="ChariotSkinOwnership"/>).
///
/// Warum überhaupt ein genetzwerktes Manager-Objekt: Die Publikumsgunst (PG) liegt host-autoritativ im
/// <see cref="PublicityCurrencyManager"/> – ein Client kann seine PG nicht selbst abbuchen. Der Kauf muss
/// also über den Host laufen. Der Ablauf ist bewusst 1:1 wie <see cref="AltarUpgradeManager.RequestPurchase"/>:
/// der Host wendet den Kauf direkt an, ein Client bittet den Host per <see cref="RequestBuyRpc"/> und nennt
/// dabei AUSDRÜCKLICH sich selbst als Käufer (nicht über den mehrdeutigen Rpc.Caller-Kontext).
///
/// Der BESITZ selbst ist rein lokal + auf Platte gespeichert (<see cref="ChariotSkinOwnership"/>), genau wie
/// die Skin-WAHL (<see cref="ChariotSkinPreference"/>): andere Peers interessiert nur der tatsächlich
/// ausgerüstete Skin. Der Host bucht also nur die PG ab und meldet dem Käufer per
/// <see cref="ConfirmOwnedRpc"/> "bezahlt – schalt ihn frei"; das Freischalten/Speichern macht dann der
/// Käufer lokal.
///
/// Auto-gespawnt via <see cref="EnsureExists"/> aus <see cref="GameNetworkManager"/> (Lobby) – gleiches
/// host-gegatetes, idempotentes Muster wie die anderen Manager, also KEINE Editor-Platzierung nötig.
/// </summary>
public sealed class ChariotSkinShop : Component
{
	public static ChariotSkinShop Instance { get; private set; }

	/// <summary>
	/// Preis des Krokodilkopfs in PG. Einzige Quelle der Wahrheit – der Host bucht genau das ab, und die
	/// CharacterGUI zeigt genau das an (<see cref="ChariotSkinShop.PriceFor"/>). Zum Ändern hier anfassen.
	/// </summary>
	public const int KrokodilkopfPrice = 80;

	// True auf dem Host ODER wenn gar keine Netzwerk-Session läuft (Solo im Editor) – gleiche
	// Autoritäts-Definition wie AltarUpgradeManager / PublicityCurrencyManager.
	static bool IsAuthority => !Networking.IsActive || Networking.IsHost;

	/// <summary> Preis eines Skins in PG. Basis-Skin (Pferdekopf) = 0 (nichts zu kaufen). </summary>
	public static int PriceFor( ChariotSkin skin )
		=> skin == ChariotSkin.Krokodilkopf ? KrokodilkopfPrice : 0;

	protected override void OnAwake()
	{
		Instance = this;

		if ( Networking.IsHost )
			GameObject.NetworkSpawn();
	}

	protected override void OnDestroy()
	{
		if ( Instance == this ) Instance = null;
	}

	/// <summary>
	/// Idempotent: erstellt+spawnt einen Shop in der Szene, falls keiner da ist. Von jedem Peer
	/// aufrufbar; nur der Host legt das GameObject wirklich an. Wie <see cref="PublicityCurrencyManager.EnsureExists"/>.
	/// </summary>
	public static void EnsureExists( Scene scene )
	{
		if ( !Networking.IsHost ) return;
		if ( Instance.IsValid() ) return;
		if ( scene is null ) return;

		var go = scene.CreateObject( enabled: true );
		go.Name = "ChariotSkinShop";
		go.Components.Create<ChariotSkinShop>();
	}

	// ---------- Kauf ----------

	/// <summary>
	/// Der lokale Spieler kauft <paramref name="skin"/> für sich selbst. Einstiegspunkt der CharacterGUI.
	/// Der Host wendet den Kauf direkt an, ein Client fragt den Host per RPC. Guards (Preis &gt; 0, nicht
	/// schon besessen) sind lokales Sofort-Feedback; der Host prüft die PG beim Abbuchen erneut.
	/// </summary>
	public static void RequestBuy( ChariotSkin skin )
	{
		int price = PriceFor( skin );
		if ( price <= 0 ) return;                       // Basis-Skin – nichts zu kaufen
		if ( ChariotSkinOwnership.IsOwned( skin ) ) return; // schon freigeschaltet

		ulong steamId = PublicityCurrencyManager.LocalSteamId;
		if ( steamId == 0 ) return;

		var mgr = Instance;
		if ( !mgr.IsValid() )
		{
			Log.Warning( "[ChariotSkinShop] Kein Manager in der Szene – Kauf geht ins Leere." );
			return;
		}

		if ( IsAuthority )
			mgr.ApplyBuy( steamId, (int)skin );
		else
			mgr.RequestBuyRpc( steamId, (int)skin );
	}

	[Rpc.Host]
	public void RequestBuyRpc( ulong steamId, int skinId )
	{
		if ( !IsAuthority ) return;

		// Käufer ist die vom Aufrufer mitgeschickte ID, nicht der mehrdeutige RPC-Kontext. Rpc.Caller wird
		// nur zum ABLEHNEN genutzt: sagt uns die Engine, wer ruft, darf ein Client nur für sich selbst
		// kaufen (gleiche Absicherung wie AltarUpgradeManager.RequestPurchaseRpc).
		if ( Rpc.Calling && Rpc.Caller is not null && Rpc.Caller.SteamId != steamId )
		{
			Log.Warning( $"[ChariotSkinShop] {Rpc.Caller.SteamId} wollte für {steamId} kaufen – abgelehnt." );
			return;
		}

		ApplyBuy( steamId, skinId );
	}

	// Host-seitig: PG abbuchen und den Käufer freischalten lassen.
	void ApplyBuy( ulong steamId, int skinId )
	{
		if ( !IsAuthority ) return;
		if ( steamId == 0 ) return;

		var skin = (ChariotSkin)skinId;
		int price = PriceFor( skin );
		if ( price <= 0 ) return;

		// PublicityCurrencyManager.TryModify ist host-only und schlägt bei zu wenig PG fehl -> hier ist der
		// einzige Ort, an dem der Kauf wirklich Geld kostet.
		if ( !PublicityCurrencyManager.TryModify( steamId, -price ) )
		{
			Log.Info( $"[ChariotSkinShop] {steamId} kann {skin} nicht kaufen (zu wenig PG / {price} PG)." );
			return;
		}

		Log.Info( $"[ChariotSkinShop] {steamId} kauft {skin} für {price} PG." );

		// Broadcast läuft auch lokal auf dem Host -> der Host-Käufer schaltet damit ebenfalls frei.
		ConfirmOwnedRpc( steamId, skinId );
	}

	/// <summary>
	/// Host -&gt; alle: "Spieler <paramref name="steamId"/> hat <paramref name="skinId"/> bezahlt." Läuft auf
	/// jedem Peer, aber nur der KÄUFER schaltet lokal frei (Besitz ist ein lokaler, auf Platte gespeicherter
	/// Stand). Andere Peers ignorieren es – sie brauchen den Besitz-Stand fremder Spieler nie.
	/// </summary>
	[Rpc.Broadcast]
	public void ConfirmOwnedRpc( ulong steamId, int skinId )
	{
		if ( steamId != PublicityCurrencyManager.LocalSteamId ) return;
		ChariotSkinOwnership.MarkOwned( (ChariotSkin)skinId );
	}
}
