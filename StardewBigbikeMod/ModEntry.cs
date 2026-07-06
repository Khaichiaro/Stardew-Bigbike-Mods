using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewValley.Buffs;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Characters;
using StardewValley.GameData;
using StardewValley.GameData.Buildings;
using StardewValley.Menus;

namespace StardewBigbikeMod
{
    /// <summary>Mod เพิ่ม "โรงรถบิ๊กไบค์" ซื้อได้จาก Robin — ข้างในคือคอกม้าที่ spawn ม้า แล้วเราสลับ sprite ม้าตัวนั้นเป็นบิ๊กไบค์</summary>
    public class ModEntry : Mod
    {
        /// <summary>id โรงรถ BMW S1000RR (สีดำ) ใน Data/Buildings</summary>
        private const string GarageBuildingId = "Khaichiaro.BigBike_Garage";

        /// <summary>id โรงรถ Ducati V4S (สีแดง)</summary>
        private const string GarageV4SId = "Khaichiaro.BigBike_Garage_V4S";

        /// <summary>key ใน modData ที่ใช้แท็กว่าม้าตัวนี้คือบิ๊กไบค์</summary>
        private const string BikeFlagKey = "Khaichiaro.BigBike/IsBike";

        /// <summary>ชื่อ asset ของ texture โรงรถ (ผ่าน content pipeline)</summary>
        private const string GarageTextureName = "Mods/Khaichiaro.BigBike/Garage";

        /// <summary>ชื่อ asset ของ texture บิ๊กไบค์ (สีดำ = ค่าเริ่มต้น)</summary>
        private const string BikeTextureName = "Mods/Khaichiaro.BigBike/BigBike";

        /// <summary>ชื่อ asset ของ texture Ducati V4S (แดง)</summary>
        private const string BikeTextureRed = "Mods/Khaichiaro.BigBike/BigBikeRed";

        /// <summary>key ใน modData เก็บรุ่นรถ ("s1000rr" = BMW ดำ / "v4s" = Ducati แดง)</summary>
        private const string ModelKey = "Khaichiaro.BigBike/Model";

        /// <summary>ค่าตั้งจาก config.json</summary>
        private ModConfig Config = null!;

        /// <summary>texture วงกลม HUD บอกเกียร์ (สร้างตอน GameLaunched)</summary>
        private Texture2D? GearHudTexture;

        // หมายเหตุ: สถานะ "ต่อผู้เล่น" ต้องใช้ PerScreen — split-screen มีผู้เล่นหลายคนใน process เดียว
        // ถ้าเก็บเป็น field เดียวจะตีกัน (คนขี่ของ screen 1 กับ screen 2 เขียนทับกัน)

        /// <summary>tick ที่แล้วผู้เล่นขี่บิ๊กไบค์อยู่ไหม (ไว้รีเซ็ต offset ตอนลงจากรถ)</summary>
        private readonly PerScreen<bool> WasRidingBike = new();

        /// <summary>รถคันล่าสุดที่เราขับ (ไว้ดีดคนซ้อนตอนเราลงจากรถ)</summary>
        private readonly PerScreen<Guid> LastRiddenBikeId = new();

        /// <summary>tick ที่แล้วเราซ้อนท้ายอยู่ไหม (วาล์วกันค้าง: หลุดจากสถานะซ้อนเมื่อไหร่ ปลดล็อกตัวละครเสมอ)</summary>
        private readonly PerScreen<bool> WasPassenger = new();

        /// <summary>กำลัง fade จอดำเพื่อวาร์ปคนซ้อนตามคนขับอยู่ไหม (กัน trigger fade ซ้ำทุก tick)</summary>
        private readonly PerScreen<bool> WarpingPassenger = new();

        public override void Entry(IModHelper helper)
        {
            this.Config = helper.ReadConfig<ModConfig>();

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.Content.AssetRequested += this.OnAssetRequested;
            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.World.BuildingListChanged += this.OnBuildingListChanged;
            helper.Events.Player.Warped += this.OnWarped;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.Display.MenuChanged += this.OnMenuChanged;
            helper.Events.Display.RenderedHud += this.OnRenderedHud;

            // ระบบคนซ้อนท้าย
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.Multiplayer.ModMessageReceived += this.OnModMessageReceived;
            helper.Events.Multiplayer.PeerConnected += this.OnPeerConnected;
            helper.Events.Multiplayer.PeerDisconnected += this.OnPeerDisconnected;
            helper.Events.GameLoop.SaveLoaded += this.ResetPassengerState;
            helper.Events.GameLoop.ReturnedToTitle += this.ResetPassengerState;

            // patch การวาดของ Horse: วาดตัวรถซ้ำอีกชั้น "ทับคนขี่" ตอนหันหน้าลง ให้คนดูเหมือนนั่งอยู่หลังรถ
            var harmony = new Harmony(this.ModManifest.UniqueID);
            harmony.Patch(
                original: AccessTools.Method(typeof(Horse), nameof(Horse.draw), new[] { typeof(SpriteBatch) }),
                postfix: new HarmonyMethod(typeof(ModEntry), nameof(AfterHorseDraw))
            );
        }

        /// <summary>ลงทะเบียน texture ของเรา + เพิ่มโรงรถเข้า Data/Buildings</summary>
        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo(GarageTextureName))
            {
                e.LoadFromModFile<Texture2D>("assets/garage.png", AssetLoadPriority.Exclusive);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo(BikeTextureName))
            {
                e.LoadFromModFile<Texture2D>("assets/bigbike.png", AssetLoadPriority.Exclusive);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo(BikeTextureRed))
            {
                e.LoadFromModFile<Texture2D>("assets/bigbike_red.png", AssetLoadPriority.Exclusive);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo("Data/AudioChanges"))
            {
                // ลงทะเบียนเสียงเครื่องยนต์ทั้งหมดเข้า sound bank ของเกม
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, AudioCueData>().Data;
                    string dir = Path.Combine(this.Helper.DirectoryPath, "assets");
                    void Reg(string id, string file, bool loop)
                    {
                        data[id] = new AudioCueData
                        {
                            Id = id,
                            Category = "Sound",
                            Looped = loop,
                            FilePaths = new List<string> { Path.Combine(dir, file) },
                        };
                    }
                    Reg(CueStart, "engine_start.wav", false);       // สตาร์ทมือ (ครั้งแรก)
                    Reg(CueIdle, "engine_loop.wav", true);          // เดินเบา — นิ่งทุกเกียร์
                    Reg(CueRev, "Burn_S1000RR.wav", true);          // เบิ้ลเครื่อง (loop ตอนกดค้าง)
                    // ไล่เกียร์: เสียงเต็มไฟล์ (เล่นจบ = ขึ้นเกียร์ถัดไป)
                    Reg(CueGear1, "Increase_speed_S1000RR.wav", false);
                    Reg(CueGear2, "Chain_gear_up_to_2_S1000RR.wav", false);
                    Reg(CueGear3, "Chain_gear_up_to_3_S1000RR.wav", false);
                    Reg(CueGear4, "Chain_gear_up_to_4_S1000RR.wav", false);
                    Reg(CueGear6Loop, "Chain4_tail.wav", true);     // เกียร์ 6 วนต่อเนื่อง (ยืดจาก Chain 4)
                });
            }
            else if (e.NameWithoutLocale.IsEquivalentTo("Data/Buildings"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, BuildingData>().Data;

                    // สร้าง building option 1 อัน (เหมือนกันหมด ต่างแค่ชื่อ+รูปตามรุ่น)
                    BuildingData MakeGarage(string nameKey, string texture) => new BuildingData
                    {
                        Name = this.Helper.Translation.Get(nameKey),
                        Description = this.Helper.Translation.Get("garage.description"),
                        Texture = texture,
                        Builder = "Robin",
                        BuildCost = this.Config.BuildCost,
                        // วัสดุสร้างโรงรถ
                        // 🔧 DEV: เปลี่ยนวัสดุตรงนี้ — Wood=(O)388, Stone=(O)390, Iron Bar=(O)335, Iridium=(O)337, Battery=(O)787
                        BuildMaterials = new List<BuildingMaterial>
                        {
                            // new() { ItemId = "(O)388", Amount = 1 }, // ไม้ (Wood)
                            // new() { ItemId = "(O)390", Amount = 1 }, // หิน (Stone)
                            new() { ItemId = "(O)335", Amount = this.Config.IronBars },     // เหล็กแท่ง
                            new() { ItemId = "(O)337", Amount = this.Config.IridiumBars },  // อีรีเดียมแท่ง
                            new() { ItemId = "(O)787", Amount = this.Config.Batteries },    // แบตเตอรี่
                        },
                        BuildDays = this.Config.BuildDays,
                        Size = new Point(4, 2),
                        BuildingType = "StardewValley.Buildings.Stable", // ยืมกลไกคอกม้า (spawn/ขี่)
                        HumanDoor = new Point(-1, -1),
                        SortTileOffset = 1f,           // วาดหลังตัวละคร/รถแถวประตู
                        CollisionMap = "XXXX\nXOOX",   // ช่องประตูเดินทะลุได้
                    };

                    // 2 รุ่นให้เลือกในเมนู Robin (เลื่อนซ้าย-ขวาเลือกได้ตามปกติของเกม)
                    data[GarageBuildingId] = MakeGarage("garage.name.s1000rr", GarageTextureName);
                    data[GarageV4SId] = MakeGarage("garage.name.v4s", GarageTextureName);
                });
            }
        }

        /// <summary>ลงทะเบียนหน้า setting กับ Generic Mod Config Menu (ถ้าผู้เล่นติดตั้งไว้)</summary>
        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            // สร้าง texture วงกลม HUD บอกเกียร์ (pixel-art: ขอบม่วง + ในดำโปร่ง)
            this.GearHudTexture = this.MakeGearHudTexture();

            var gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm is null)
                return; // ไม่มี GMCM ก็ไม่เป็นไร — แก้ config.json ตรงๆ ได้เหมือนเดิม

            gmcm.Register(
                mod: this.ModManifest,
                reset: () => this.Config = new ModConfig(),
                save: () =>
                {
                    this.Helper.WriteConfig(this.Config);
                    // Data/Buildings ถูก cache ไว้ — ต้องสั่งโหลดใหม่เพื่อให้ราคา/วัสดุ/วันสร้างอัปเดตทันที
                    this.Helper.GameContent.InvalidateCache("Data/Buildings");
                }
            );

            gmcm.AddNumberOption(
                mod: this.ModManifest,
                getValue: () => this.Config.BuildCost,
                setValue: value => this.Config.BuildCost = value,
                name: () => this.Helper.Translation.Get("config.buildCost.name"),
                tooltip: () => this.Helper.Translation.Get("config.buildCost.tooltip"),
                min: 0,
                max: 500000,
                interval: 500
            );
            gmcm.AddNumberOption(
                mod: this.ModManifest,
                getValue: () => this.Config.IronBars,
                setValue: value => this.Config.IronBars = value,
                name: () => this.Helper.Translation.Get("config.iron.name"),
                tooltip: () => this.Helper.Translation.Get("config.iron.tooltip"),
                min: 0, max: 999);
            gmcm.AddNumberOption(
                mod: this.ModManifest,
                getValue: () => this.Config.IridiumBars,
                setValue: value => this.Config.IridiumBars = value,
                name: () => this.Helper.Translation.Get("config.iridium.name"),
                tooltip: () => this.Helper.Translation.Get("config.iridium.tooltip"),
                min: 0, max: 999);
            gmcm.AddNumberOption(
                mod: this.ModManifest,
                getValue: () => this.Config.Batteries,
                setValue: value => this.Config.Batteries = value,
                name: () => this.Helper.Translation.Get("config.battery.name"),
                tooltip: () => this.Helper.Translation.Get("config.battery.tooltip"),
                min: 0, max: 999);
            gmcm.AddNumberOption(
                mod: this.ModManifest,
                getValue: () => this.Config.BuildDays,
                setValue: value => this.Config.BuildDays = value,
                name: () => this.Helper.Translation.Get("config.buildDays.name"),
                tooltip: () => this.Helper.Translation.Get("config.buildDays.tooltip"),
                min: 0,
                max: 28
            );
            // ---- ปุ่มควบคุม ----
            void AddKey(string key, Func<KeybindList> get, Action<KeybindList> set)
            {
                gmcm.AddKeybindList(this.ModManifest, get, set,
                    () => this.Helper.Translation.Get($"config.{key}.name"),
                    () => this.Helper.Translation.Get($"config.{key}.tooltip"));
            }
            AddKey("engineOff", () => this.Config.EngineOffKey, v => this.Config.EngineOffKey = v);
            AddKey("rev", () => this.Config.RevKey, v => this.Config.RevKey = v);

            // ---- ความเร็ว (ต่ำสุด=เกียร์ 1, สูงสุด=เกียร์ 6) + ระดับเสียง ----
            gmcm.AddNumberOption(this.ModManifest,
                () => this.Config.MinSpeed, v => this.Config.MinSpeed = v,
                () => this.Helper.Translation.Get("config.minSpeed.name"),
                () => this.Helper.Translation.Get("config.minSpeed.tooltip"),
                min: 0f, max: 20f, interval: 0.5f);
            gmcm.AddNumberOption(this.ModManifest,
                () => this.Config.MaxSpeed, v => this.Config.MaxSpeed = v,
                () => this.Helper.Translation.Get("config.maxSpeed.name"),
                () => this.Helper.Translation.Get("config.maxSpeed.tooltip"),
                min: 0f, max: 30f, interval: 0.5f);
            gmcm.AddNumberOption(this.ModManifest,
                () => this.Config.EngineVolume, v => this.Config.EngineVolume = v,
                () => this.Helper.Translation.Get("config.volume.name"),
                () => this.Helper.Translation.Get("config.volume.tooltip"),
                min: 0f, max: 2f, interval: 0.1f);
        }

        /// <summary>ตำแหน่งคนขับบนรถ ต่อทิศ (จูนเสร็จแล้ว fix ค่าถาวร) — คืนค่า (xOffset, yOffset)</summary>
        private static (float x, float y) DriverSeat(int facingDirection) => facingDirection switch
        {
            2 => (-5.5f, 5f),   // หันหน้าลง
            0 => (-5.5f, -15f), // หันหลัง
            1 => (1f, 0f),      // หันขวา
            _ => (-12f, 0f),    // หันซ้าย
        };

        /// <summary>ตำแหน่งคนซ้อนท้าย ต่อทิศ (จูนเสร็จแล้ว fix ค่าถาวร — ทุกเครื่องใช้ค่าเดียวกันจะได้เห็นตรงกัน)</summary>
        private static (float x, float y) PassengerSeat(int facingDirection) => facingDirection switch
        {
            2 => (-5f, 70f),   // หันหน้าลง
            0 => (-5f, 15f),   // หันหลัง
            1 => (13f, 97f),   // หันขวา
            _ => (-25f, 97f),  // หันซ้าย
        };

        /// <summary>จังหวะโยกตามอนิเมชันของรถ (สูตรเดียวกับ Farmer.showRiding ของเกม)</summary>
        private static float BikeBob(Horse bike)
        {
            return bike.Sprite?.CurrentAnimation is null
                ? 0f
                : bike.Sprite.currentAnimationIndex switch { 1 or 2 => -4f, 4 or 5 => 4f, _ => 0f };
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            this.UpdateAllBikes(parkInFront: true);
        }

        /// <summary>เผื่อกรณีโรงรถสร้างเสร็จ/ถูกย้าย ระหว่างวัน</summary>
        private void OnBuildingListChanged(object? sender, BuildingListChangedEventArgs e)
        {
            this.UpdateAllBikes(parkInFront: false);
        }

        /// <summary>sprite ของม้าอาจถูกเกมรีเซ็ตตอนเปลี่ยนแมพ — ทาทับใหม่ทุกครั้งที่ warp</summary>
        private void OnWarped(object? sender, WarpedEventArgs e)
        {
            foreach (Horse horse in e.NewLocation.characters.OfType<Horse>())
            {
                if (horse.modData.ContainsKey(BikeFlagKey))
                    this.ApplyBikeSprite(horse);
            }
        }

        /// <summary>ไล่หาโรงรถทุกหลัง แท็กม้าของมันเป็นบิ๊กไบค์ สลับ sprite และจัดที่จอด</summary>
        private void UpdateAllBikes(bool parkInFront)
        {
            if (!Context.IsWorldReady)
                return;

            Utility.ForEachBuilding(building =>
            {
                // ระบุรุ่นรถจากชนิดโรงรถที่สร้าง (BMW = โรงรถ S1000RR / Ducati = โรงรถ V4S)
                string? model = building.buildingType.Value switch
                {
                    GarageBuildingId => "s1000rr",
                    GarageV4SId => "v4s",
                    _ => null,
                };
                if (model is not null && building is Stable garage)
                {
                    Horse? bike = garage.getStableHorse();
                    if (bike is not null)
                    {
                        bike.modData[BikeFlagKey] = "true";
                        bike.modData[ModelKey] = model; // ผูกรุ่นตามโรงรถ
                        this.ApplyBikeSprite(bike);

                        // จอดในช่องประตูโรงรถ (จุด spawn เดิมของคอกม้า) หันข้างโชว์ตัวรถ
                        if (parkInFront && Game1.IsMasterGame && bike.rider is null
                            && bike.TilePoint == garage.GetDefaultHorseTile())
                        {
                            bike.faceDirection(1);
                        }
                    }
                }
                return true; // หาต่อจนครบทุกหลัง
            });
        }

        /// <summary>จัดระเบียบ sprite ของบิ๊กไบค์ทุก tick:
        /// 1) ตอนหยุดนิ่ง เกมจะค้างเฟรมที่คอลัมน์ล่าสุดของท่าวิ่ง (มีควัน/รถโยก) → บังคับกลับเฟรมยืน (คอลัมน์ 0)
        /// 2) เกมตั้ง drawOffset หันซ้าย=0 ทิศอื่น=-16 (จูนไว้กับรูปม้า) ทำให้รถเหลื่อมตอนหันซ้าย → ใช้ -16 เท่ากันทุกทิศ</summary>
        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            // จัด offset ของรถ+คนขับ ให้ "รถทุกคันที่มีคนขี่" (ไม่ใช่แค่ของเราเอง) — สำคัญมากในมัลติเพลเยอร์:
            // offset เป็นค่า render เฉพาะเครื่อง ไม่ sync → ทุกเครื่องต้องคำนวณเองให้ครบทุกคน ถึงจะเห็นตรงกัน
            var riddenBikes = new HashSet<Horse>();
            foreach (Farmer f in Game1.getOnlineFarmers())
            {
                if (f.mount is Horse m && m.modData.ContainsKey(BikeFlagKey))
                {
                    this.FixBikeSprite(m, f);
                    riddenBikes.Add(m);
                }
            }

            // ลงจากรถเมื่อไหร่ ล้าง offset ที่ mod ตั้งไว้ + ดีดคนซ้อน (ถ้ามี) ให้ลงพร้อมกันแบบสะอาดๆ
            bool ridingBike = Game1.player.mount?.modData.ContainsKey(BikeFlagKey) == true;
            if (ridingBike)
            {
                this.LastRiddenBikeId.Value = Game1.player.mount!.HorseId;
            }
            else if (this.WasRidingBike.Value)
            {
                Game1.player.xOffset = 0f;
                Game1.player.yOffset = 0f;
                Game1.player.buffs.Remove("Khaichiaro.BigBike_Speed"); // เลิกขี่ → ล้าง speed buff ของเกียร์
                // เราคือคนขับที่เพิ่งลงจากรถ → ประกาศดีดคนซ้อนบนคันนั้นให้ทุกเครื่องรับรู้
                foreach (var pair in this.Passengers.Where(kv => kv.Value == this.LastRiddenBikeId.Value).ToArray())
                    this.SetPassenger(pair.Key, pair.Value, mounted: false, broadcast: true);
            }
            this.WasRidingBike.Value = ridingBike;

            // วาล์วกันค้าง: ตรวจว่า "ซ้อนจริง" ไหม — ต้องมีคนอื่นขับรถคันที่เราซ้อนอยู่จริงๆ
            // ถ้าทะเบียนคนซ้อนค้าง (ขี่รถเอง / network สะดุด / คนขับหาย) จะทำให้ canMove=false ค้างจนกดอะไรไม่ได้
            // → เอาออกจากทะเบียนแล้วปลดล็อกทันที
            long myId = Game1.player.UniqueMultiplayerID;
            bool seated = this.Passengers.ContainsKey(myId);
            if (seated)
            {
                Guid bikeId = this.Passengers[myId];
                bool validRide = Game1.getOnlineFarmers().Any(f =>
                    f.UniqueMultiplayerID != myId && f.mount is Horse m && m.HorseId == bikeId);
                if (!validRide || Game1.player.mount is not null) // ไม่มีคนขับรถคันนั้น หรือเราขี่รถเอง = ค้าง
                {
                    this.Passengers.Remove(myId);
                    seated = false;
                }
            }
            if (!seated && this.WasPassenger.Value)
            {
                this.ReleasePassenger(Game1.player, findOpenTile: true);
                this.Monitor.Log("ปลดล็อกคนซ้อน (วาล์วกันค้าง)", LogLevel.Debug);
            }
            this.WasPassenger.Value = seated;

            // รถที่จอดในแมพ (ไม่มีคนขี่) — จัด sprite/drawOffset ให้ด้วย
            foreach (var npc in Game1.player.currentLocation?.characters ?? new Netcode.NetCollection<NPC>())
            {
                if (npc is Horse bike && !riddenBikes.Contains(bike))
                    this.FixBikeSprite(bike, null);
            }

            this.UpdatePassengers();
            this.UpdateEngine();
        }

        /// <summary>จัด sprite + drawOffset ของรถ และ (ถ้ามีคนขี่) offset ของคนขับ
        /// — รับ driver เข้ามาตรงๆ เพราะ bike.rider ไม่ sync ข้ามเครื่อง ใช้ค่าจาก farmer ที่ mount จริง</summary>
        private void FixBikeSprite(Horse bike, Farmer? driver)
        {
            if (!bike.modData.ContainsKey(BikeFlagKey) || bike.Sprite is null)
                return;
            bike.onFootstepAction = _ => { }; // ปิดเสียงกีบม้าทุกเครื่อง (รันให้รถทุกคัน) — กันบัคเสียงม้าเวลาคนอื่นขี่

            // สลับ texture ตามสีที่เลือก (ดำ/แดง) — เช็คทุก tick เผื่อสีเปลี่ยนกลางวัน
            string wantTex = GetBikeModel(bike) == "v4s" ? BikeTextureRed : BikeTextureName;
            if (bike.Sprite.textureName.Value != wantTex)
                bike.Sprite = new AnimatedSprite(wantTex, bike.Sprite.currentFrame, 32, 32);
            if (bike.Sprite.CurrentAnimation is null && bike.Sprite.currentFrame % 7 != 0)
            {
                bike.Sprite.currentFrame -= bike.Sprite.currentFrame % 7;
                bike.Sprite.UpdateSourceRect();
            }

            // แกน X: เกมเดิมหันซ้าย=0 ทิศอื่น=-16 (จูนกับรูปม้า) → ใช้ -16 เท่ากันทุกทิศ รถจะไม่เหลื่อมซ้าย-ขวา
            // แกน Y: เฟรมยืนมุมข้างในไฟล์รูปวาดสูงกว่าเฟรมวิ่ง 1px (เป็นจังหวะโยกของอาร์ต)
            //        → ตอนยืนนิ่งหันข้าง กดตำแหน่งวาดลง 4 หน่วยโลก (= 1px sprite) ให้ฐานล้อตรงกับตอนวิ่ง
            bool standingSideways = (bike.FacingDirection == 1 || bike.FacingDirection == 3)
                && (bike.Sprite.CurrentAnimation is null || bike.Sprite.currentFrame >= 21);
            bike.drawOffset = new Vector2(-16f, standingSideways ? 4f : 0f);

            // ตำแหน่งคนขับ (ค่า fix ที่จูนเสร็จแล้ว) — คำนวณจังหวะโยกเองแล้ว "ทับ" ค่าไปเลย
            // (ห้าม += เพราะช่วงอนิเมชันลงจากรถ เกมหยุดรีเซ็ตค่า ทำให้ทบต้นจนตัวละครลอยหลุดจอ)
            if (driver is not null)
            {
                (float x, float y) = DriverSeat(driver.FacingDirection);
                driver.xOffset = x;
                driver.yOffset = (driver.isMoving() ? BikeBob(bike) : 0f) + y;
            }
        }

        // ==================== ระบบคนซ้อนท้าย (multiplayer — ทุกคนต้องลง mod นี้) ====================

        /// <summary>ชนิดข้อความ sync สถานะคนซ้อนข้ามเครื่อง</summary>
        private const string PassengerMsgType = "PassengerState";

        /// <summary>ทะเบียนคนซ้อน: id ผู้เล่น → HorseId ของรถที่ซ้อนอยู่ (ทุกเครื่องถือสำเนาเดียวกันผ่าน ModMessage)</summary>
        private readonly Dictionary<long, Guid> Passengers = new();

        /// <summary>กดปุ่ม action: ซ้อนอยู่ = ลงจากรถ / ยืนข้างรถที่มีคนขับ = ขึ้นซ้อน</summary>
        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady || !e.Button.IsActionButton())
                return;

            // กำลังซ้อนอยู่ → ลง
            if (this.Passengers.ContainsKey(Game1.player.UniqueMultiplayerID))
            {
                this.DismountPassenger();
                this.Helper.Input.Suppress(e.Button);
                return;
            }

            // ขี่รถเองอยู่ / มีสัตว์ขี่อยู่ → ไม่เกี่ยว
            if (Game1.player.mount is not null)
                return;

            // หา "รถที่มีคนขับ" ใกล้ตัวผู้เล่น (รัศมี 2.5 ช่องจากตัวรถ) หรือคลิกที่ตัวรถตรงๆ
            Vector2 cursorTile = e.Cursor.GrabTile;
            foreach (Farmer other in Game1.getOnlineFarmers())
            {
                if (other.UniqueMultiplayerID == Game1.player.UniqueMultiplayerID)
                    continue;
                if (other.currentLocation != Game1.player.currentLocation)
                    continue;
                if (other.mount is not Horse bike)
                {
                    this.Monitor.Log($"ซ้อนไม่ได้: {other.Name} ไม่ได้ขี่พาหนะ (mount=null)", LogLevel.Debug);
                    continue;
                }
                if (!bike.modData.ContainsKey(BikeFlagKey))
                {
                    this.Monitor.Log($"ซ้อนไม่ได้: {other.Name} ขี่ม้าธรรมดา ไม่ใช่บิ๊กไบค์", LogLevel.Debug);
                    continue;
                }
                if (this.Passengers.ContainsValue(bike.HorseId))
                {
                    this.Monitor.Log("ซ้อนไม่ได้: เบาะหลังไม่ว่าง", LogLevel.Debug);
                    continue;
                }
                // ต้องอยู่ติดรถ 1 ช่อง (รวมแนวทแยง) หรือคลิกที่ตัวรถตรงๆ
                bool adjacent = Math.Abs(bike.TilePoint.X - Game1.player.TilePoint.X) <= 1
                    && Math.Abs(bike.TilePoint.Y - Game1.player.TilePoint.Y) <= 1;
                float distToCursor = Vector2.Distance(bike.Tile, cursorTile);
                if (adjacent || distToCursor <= 1f)
                {
                    this.Monitor.Log($"ขึ้นซ้อนท้ายรถของ {other.Name}", LogLevel.Debug);
                    this.SetPassenger(Game1.player.UniqueMultiplayerID, bike.HorseId, mounted: true, broadcast: true);
                    this.Helper.Input.Suppress(e.Button);
                    return;
                }
                this.Monitor.Log("ซ้อนไม่ได้: ต้องยืนติดรถ 1 ช่อง", LogLevel.Debug);
            }
        }

        /// <summary>หา horse ตาม id — สำคัญ: Utility.findHorse ของเกมหาเฉพาะตัวที่จอดในแมพ
        /// รถที่กำลังถูกขี่จะเกาะอยู่กับตัวคนขับ ต้องไล่เช็ค mount ของผู้เล่นทุกคนเอง</summary>
        private static Horse? FindBike(Guid horseId)
        {
            foreach (Farmer f in Game1.getOnlineFarmers())
            {
                if (f.mount is Horse m && m.HorseId == horseId)
                    return m;
            }
            return Utility.findHorse(horseId);
        }

        /// <summary>อัปเดต/บันทึกสถานะคนซ้อน แล้วกระจายให้เครื่องอื่นถ้าต้องการ</summary>
        private void SetPassenger(long playerId, Guid horseId, bool mounted, bool broadcast)
        {
            bool changed = mounted != this.Passengers.ContainsKey(playerId);
            if (mounted)
                this.Passengers[playerId] = horseId;
            else
                this.Passengers.Remove(playerId);

            // SetPassenger รันทุกเครื่อง (ฝั่ง broadcast + ฝั่งรับ message) → จัดการ visual ให้ตรงกันทุกจอ
            if (changed)
            {
                Farmer? p = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == playerId);
                if (!mounted)
                {
                    // มีคนลงจากรถ → รีเซ็ต offset/ท่า ของ "ทุกคน" ทุกเครื่อง (ค่าพวกนี้ไม่ sync ข้ามเครื่อง
                    // ต้องล้างเองทุกจอ ไม่งั้นจอคนอื่นเห็นคนซ้อน remote ค้างลอย/ท่านั่งค้างที่ท้ายรถ)
                    foreach (Farmer f in Game1.getOnlineFarmers())
                    {
                        f.xOffset = 0f;
                        f.yOffset = 0f;
                    }
                    p?.completelyStopAnimatingOrDoingAction();
                }
                // อนิเมชันกระโดดขึ้น/ลง — ใช้ synchronizedJump ที่มี event broadcast ให้เห็นทุกจอ
                // (jump() ธรรมดาเป็น physics เฉพาะเครื่อง จอคนอื่นไม่เห็น)
                // guard IsLocalPlayer ในตัวมันจัดการเอง: เฉพาะเครื่อง local ของ p ที่ fire แล้ว broadcast ให้ที่เหลือ
                p?.synchronizedJump(8f);
            }

            if (broadcast)
            {
                this.Helper.Multiplayer.SendMessage(
                    new PassengerMsg { PlayerId = playerId, HorseId = horseId, Mounted = mounted },
                    PassengerMsgType,
                    modIDs: new[] { this.ModManifest.UniqueID });
            }
        }

        /// <summary>ผู้เล่นเครื่องนี้กดปุ่มลงจากเบาะหลัง</summary>
        private void DismountPassenger()
        {
            long id = Game1.player.UniqueMultiplayerID;
            if (!this.Passengers.TryGetValue(id, out Guid horseId))
                return;
            this.Monitor.Log("คนซ้อนลงจากรถ (กดปุ่ม)", LogLevel.Debug);
            this.SetPassenger(id, horseId, mounted: false, broadcast: true);
            this.ReleasePassenger(Game1.player, findOpenTile: true);
        }

        /// <summary>ปลดสถานะคนซ้อนออกจาก farmer object ตรงๆ (ใช้ได้ทั้ง local/split-screen):
        /// ปลดล็อกการเดิน ล้าง offset หยุดท่านั่ง แล้ว (ถ้าขอ) หาช่องว่างข้างๆ ให้ไปยืน</summary>
        private void ReleasePassenger(Farmer p, bool findOpenTile)
        {
            p.canMove = true;
            p.xOffset = 0f;
            p.yOffset = 0f;
            p.completelyStopAnimatingOrDoingAction();
            if (findOpenTile && p.currentLocation is not null)
            {
                Vector2 open = Utility.getRandomAdjacentOpenTile(p.Tile, p.currentLocation);
                if (open != Vector2.Zero)
                    p.Position = open * 64f;
            }
        }

        /// <summary>ทำงานทุก tick: ตรวจความถูกต้อง + ล็อกตำแหน่ง/ท่านั่งของคนซ้อนทุกคนบนเครื่องนี้</summary>
        private void UpdatePassengers()
        {
            if (this.Passengers.Count == 0)
                return;
            foreach (var pair in this.Passengers.ToArray())
            {
                long passengerId = pair.Key;
                Guid horseId = pair.Value;

                Farmer? p = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == passengerId);
                // หา "คนขับ" จากผู้เล่นที่ mount รถคันนี้ (ห้ามใช้ bike.rider — ค่านั้นไม่ sync ข้ามเครื่อง)
                Farmer? driver = Game1.getOnlineFarmers().FirstOrDefault(
                    f => f.mount is Horse m && m.HorseId == horseId);

                // คนซ้อนหลุดวง → ลบทิ้งเฉยๆ
                if (p is null)
                {
                    this.Passengers.Remove(passengerId);
                    continue;
                }

                // คนขับลงจากรถ/ไม่พบคนขับ → ยกเลิกการซ้อน แล้วปลดล็อกตัวคนซ้อน
                if (driver is null || driver == p)
                {
                    this.Monitor.Log($"ยกเลิกซ้อน {p.Name}: ไม่พบคนขับรถคันนี้แล้ว", LogLevel.Debug);
                    this.Passengers.Remove(passengerId);
                    this.ReleasePassenger(p, findOpenTile: p.IsLocalPlayer);
                    continue;
                }

                // เทียบแมพด้วย "ชื่อ" ไม่ใช่ reference — remote farmer ชี้คนละ instance เลย == ไม่เคยจริง
                string? pLoc = p.currentLocation?.NameOrUniqueName;
                string? dLoc = driver.currentLocation?.NameOrUniqueName;

                // คนขับอยู่คนละแมพกับคนซ้อน → พาคนซ้อน (เฉพาะจอ/เครื่องที่คุมตัวนี้) วาร์ปตามไป
                if (dLoc is not null && pLoc is not null && pLoc != dLoc)
                {
                    // Game1.warpFarmer พึ่ง static locationRequest ที่ split-screen ทุก instance แชร์กัน → ใช้ไม่ได้
                    // จึงย้าย location ของคนซ้อนเอง พร้อม fade จอดำ ปิด-เปิด ให้เนียนเหมือนคนขับข้ามแมพ
                    if (p.IsLocalPlayer && !this.WarpingPassenger.Value
                        && Game1.getLocationFromName(dLoc) is GameLocation target)
                    {
                        this.Monitor.Log($"[warp] {p.Name} {pLoc}→{dLoc}", LogLevel.Debug);
                        this.WarpingPassenger.Value = true;
                        Farmer localP = p, localDriver = driver;
                        Game1.globalFadeToBlack(() =>
                        {
                            localP.currentLocation?.cleanupBeforePlayerExit();
                            localP.currentLocation = target;
                            // สำคัญ: set Game1.currentLocation ด้วย ไม่งั้น viewport/กล้องจอคนซ้อนไม่เปลี่ยนแมพตาม
                            // (setter ผูกกับ instanceGameLocation ต่อจอ + trigger location change ให้เอง)
                            Game1.currentLocation = target;
                            localP.Position = localDriver.Position;
                            target.resetForPlayerEntry();
                            Game1.forceSnapOnNextViewportUpdate = true;
                            Game1.globalFadeToClear();
                            this.WarpingPassenger.Value = false;
                        }, 0.03f);
                    }
                    continue;
                }
                // มาถึงแมพคนขับแล้ว → เคลียร์ธงกันค้าง (เผื่อ callback ไม่ทำงานด้วยเหตุใด)
                this.WarpingPassenger.Value = false;

                // อยู่แมพเดียวกัน → ล็อกตำแหน่ง/ท่านั่งเกาะคนขับ (set บน farmer object ตรงๆ ครอบคลุมทุก screen)
                if (dLoc is not null && pLoc == dLoc)
                {
                    Horse? bike = FindBike(horseId);
                    this.ApplyPassengerSeat(p, driver, bike);
                }
            }
        }

        /// <summary>ล็อกตำแหน่ง + ท่านั่งของคนซ้อนให้เกาะ "ตัวคนขับ" (ข้อมูลที่ sync ข้ามเครื่องแน่นอน)
        /// — ตอนขี่ ตำแหน่งรถ = ตำแหน่งคนขับเป๊ะอยู่แล้ว จึงใช้แทนกันได้</summary>
        private void ApplyPassengerSeat(Farmer p, Farmer driver, Horse? bike)
        {
            int dir = driver.FacingDirection;
            (float x, float y) = PassengerSeat(dir);

            // ขยับ "ตำแหน่งจริง" ลงใต้รถเล็กน้อยเพื่อคุมลำดับการวาด (depth คิดจาก Y):
            // ให้คนซ้อนวาดทับตัวรถ/คนขับ และไม่โดนพุ่มไม้-ต้นไม้แถวนั้นวาดทับ แล้วชดเชยภาพคืนด้วย yOffset
            float depthNudge = dir == 2 ? 0f : 24f;

            p.canMove = false; // ห้ามคนซ้อนเดินเอง (set บน object ตรงๆ ได้ผลทุก screen)
            p.position.Value = driver.Position + new Vector2(0f, depthNudge);
            p.faceDirection(dir);
            // ท่านั่งเดียวกับตอนขี่: 113 = หันหลัง, 107 = หันหน้า, 106 = หันข้าง (ซ้ายใช้ flip)
            p.FarmerSprite.setCurrentSingleFrame(dir switch { 0 => 113, 2 => 107, _ => 106 }, 32000, false, dir == 3);
            p.xOffset = x;
            p.yOffset = (bike is not null ? BikeBob(bike) : 0f) + y - depthNudge;
        }

        private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (e.FromModID != this.ModManifest.UniqueID || e.Type != PassengerMsgType)
                return;
            var msg = e.ReadAs<PassengerMsg>();
            this.SetPassenger(msg.PlayerId, msg.HorseId, msg.Mounted, broadcast: false);
            // เบิ้ลเครื่อง sync ผ่าน bike.modData แล้ว ไม่ต้อง broadcast แยก
        }

        /// <summary>มีคนเพิ่งเข้าวง: ถ้าเราซ้อนอยู่ ประกาศสถานะซ้ำให้เครื่องใหม่รู้</summary>
        private void OnPeerConnected(object? sender, PeerConnectedEventArgs e)
        {
            long id = Game1.player.UniqueMultiplayerID;
            if (this.Passengers.TryGetValue(id, out Guid horseId))
                this.SetPassenger(id, horseId, mounted: true, broadcast: true);
        }

        /// <summary>มีคนหลุดวง: เอาออกจากทะเบียนคนซ้อน (ถ้าเป็นคนขับ ระบบตรวจใน UpdatePassengers จัดการเอง)</summary>
        private void OnPeerDisconnected(object? sender, PeerDisconnectedEventArgs e)
        {
            this.Passengers.Remove(e.Peer.PlayerID);
        }

        /// <summary>ล้างสถานะเมื่อเริ่ม/ออกจากเซฟ</summary>
        private void ResetPassengerState(object? sender, EventArgs e)
        {
            this.Passengers.Clear();
            this.WasRidingBike.Value = false;
            this.WasPassenger.Value = false;
            this.StopAllEngineAudio();
        }

        /// <summary>Harmony postfix ของ Horse.draw — หันหน้าลง+มีคนขี่: วาดตัวรถทั้งคันซ้ำอีกชั้นทับคนขี่
        /// (แบบเดียวกับที่เกมวาด "หัวม้า" ทับคนขี่ แต่ชิ้นนั้นถูกล็อกไว้แค่ 9x15 เลยต้อง patch วาดเองทั้งคัน)</summary>
        private static void AfterHorseDraw(Horse __instance, SpriteBatch b)
        {
            if (__instance.rider is null || __instance.FacingDirection != 2 || !__instance.modData.ContainsKey(BikeFlagKey))
                return;
            AnimatedSprite? sprite = __instance.Sprite;
            if (sprite?.Texture is null)
                return;

            // วาดทับเฉพาะตัวรถส่วนล่าง (ตัด 4 แถวบน = วินด์สกรีน) ให้หัวคนขี่ยังโผล่
            const int topSkip = 4;

            // คำนวณมุมบนซ้ายบนจอ ให้ตรงกับที่ NPC.draw วาด sprite หลัก: position - origin*scale
            Vector2 pos = __instance.getLocalPosition(Game1.viewport)
                + new Vector2(__instance.GetSpriteWidthForPositioning() * 4 / 2, __instance.GetBoundingBox().Height / 2);
            Vector2 topLeft = pos - new Vector2(sprite.SpriteWidth / 2 * 4, sprite.SpriteHeight * 3f / 4f * 4f);

            b.Draw(
                sprite.Texture,
                topLeft + new Vector2(0f, topSkip * 4),
                new Rectangle(sprite.SourceRect.X, sprite.SourceRect.Y + topSkip, sprite.SpriteWidth, sprite.SpriteHeight - topSkip),
                Color.White,
                0f,
                Vector2.Zero,
                4f,
                SpriteEffects.None,
                (__instance.Position.Y + 64f) / 10000f);
        }

        /// <summary>เกมเปิด dialog "ตั้งชื่อม้า" ตอนขี่ครั้งแรก — ถ้าตัวที่ตั้งชื่อคือบิ๊กไบค์ เปลี่ยนหัวข้อเป็นตั้งชื่อรถ</summary>
        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            if (e.NewMenu is not NamingMenu menu || e.NewMenu == e.OldMenu)
                return;
            bool nearBike = Game1.player.currentLocation?.characters.OfType<Horse>().Any(x =>
                x.modData.ContainsKey(BikeFlagKey)
                && Math.Abs(x.TilePoint.X - Game1.player.TilePoint.X) <= 3
                && Math.Abs(x.TilePoint.Y - Game1.player.TilePoint.Y) <= 3) == true;
            if (!nearBike)
                return;
            string horseTitle = Game1.content.LoadString("Strings\\Characters:NameYourHorse");
            if (menu.title != horseTitle)
                return; // ไม่ใช่ dialog ตั้งชื่อม้า

            // เปลี่ยนหัวข้อเป็น "ตั้งชื่อรถ" เฉยๆ (รุ่นเลือกตอนสร้างกับ Robin แล้ว ไม่ต้องถามซ้ำ)
            Game1.activeClickableMenu = new NamingMenu(
                menu.doneNaming,
                this.Helper.Translation.Get("bike.nameTitle"),
                this.Helper.Translation.Get("bike.defaultName"));
        }

        /// <summary>รุ่นรถจาก modData ("s1000rr" ค่าเริ่มต้น = BMW ดำ / "v4s" = Ducati แดง)</summary>
        private static string GetBikeModel(Horse bike) =>
            bike.modData.TryGetValue(ModelKey, out string? m) ? m : "s1000rr";

        /// <summary>สลับ sprite sheet ของม้าเป็นบิ๊กไบค์ (เฟรม 32x32) ตามสีที่เลือก + ปิดเสียงฝีเท้าม้า</summary>
        private void ApplyBikeSprite(Horse bike)
        {
            string wantTex = GetBikeModel(bike) == "v4s" ? BikeTextureRed : BikeTextureName;
            if (bike.Sprite?.textureName.Value != wantTex)
                bike.Sprite = new AnimatedSprite(wantTex, 0, 32, 32);
            bike.onFootstepAction = _ => { }; // บิ๊กไบค์ไม่มีกีบเท้า — ใช้เสียงเครื่องยนต์แทน
        }

        // ==================== ระบบเครื่องยนต์ + เกียร์ 6 สปีด (multiplayer sync) ====================
        //
        // แนวคิด sync: gear + engineOn เก็บใน bike.modData (sync ทุกเครื่องอัตโนมัติ)
        // แต่ละเครื่อง "เล่นเสียงเอง" ให้รถทุกคันในแมพตัวเอง (volume ตามระยะจากผู้เล่นในเครื่องนั้น)
        // → ทุกคนในแมพเดียวกันได้ยินเสียงเครื่องเหมือนกัน ไม่ใช่เสียงกีบม้า

        private const string CueStart = "Khaichiaro.BigBike_Start";
        private const string CueIdle = "Khaichiaro.BigBike_Idle";       // เดินเบา — ใช้ตอนนิ่งทุกเกียร์
        private const string CueRev = "Khaichiaro.BigBike_Rev";
        // ไล่เกียร์อัตโนมัติ: เสียงแต่ละเกียร์เล่นเต็มไฟล์ จบแล้วขึ้นเกียร์ถัดไป (เกียร์ 5-6 = เกียร์ 4 + pitch)
        private const string CueGear1 = "Khaichiaro.BigBike_Gear1";     // Increase_speed
        private const string CueGear2 = "Khaichiaro.BigBike_Gear2";     // Chain 2
        private const string CueGear3 = "Khaichiaro.BigBike_Gear3";     // Chain 3
        private const string CueGear4 = "Khaichiaro.BigBike_Gear4";     // Chain 4 (ใช้เกียร์ 4-6)
        private const string CueGear6Loop = "Khaichiaro.BigBike_Gear6Loop"; // เกียร์ 6 วนต่อเนื่อง (Chain4 ยืด)

        private const string GearKey = "Khaichiaro.BigBike/Gear";
        private const string EngineOnKey = "Khaichiaro.BigBike/EngineOn";
        private const string RevingKey = "Khaichiaro.BigBike/Reving"; // เบิ้ลเครื่องอยู่ไหม (sync ผ่าน modData)

        /// <summary>สถานะเสียงต่อรถ 1 คัน (per-machine — แต่ละเครื่องเล่นเสียงเองไม่ sync)</summary>
        private sealed class BikeAudio
        {
            public ICue? Loop;         // เสียงลูป (idle เดินเบา หรือ loop เกียร์ 6)
            public ICue? Main;         // เสียงเกียร์ที่กำลังไล่ (เล่นจบ = ขึ้นเกียร์ถัดไป)
            public ICue? Rev;          // เสียงเบิ้ลเครื่อง (loop ตอนกดค้าง)
            public ICue? FadeLoop;     // เดินเบาที่กำลัง fade ตอนออกตัว (crossfade idle→เกียร์ 1)
            public float RevFade = 1f;
            public float FadeVol = 1f;
            public string Sound = "";  // "idle" หรือ "gear"
            public int PlayGear;       // เกียร์ที่กำลังเล่นเสียงอยู่ (0 = ยังไม่เริ่ม/idle)
            public bool Started;
            public int StartDelay;
            public int LastMovingTick; // debounce กัน false stop ตอน turn/ติดหิน
        }

        /// <summary>ความเร็วปัจจุบัน (ไต่นุ่มๆ ไปตามเกียร์)</summary>
        private readonly PerScreen<float> CurrentSpeed = new();

        /// <summary>ทะเบียนเสียงรถทุกคันที่เครื่องนี้กำลังเล่น</summary>
        private readonly Dictionary<Guid, BikeAudio> BikeAudios = new();

        /// <summary>จำนวนเกียร์สูงสุด</summary>
        private const int MaxGear = 6;

        private static int GetGear(Horse bike) =>
            bike.modData.TryGetValue(GearKey, out string? g) && int.TryParse(g, out int v) ? Math.Clamp(v, 1, MaxGear) : 1;

        private static bool IsEngineOn(Horse bike) =>
            bike.modData.TryGetValue(EngineOnKey, out string? v) && v == "true";

        /// <summary>ทุก tick: จัดการอินพุตเกียร์/เบิ้ล/ดับ (คนขับ local) + เล่นเสียงรถทุกคันในแมพ + ปรับความเร็ว</summary>
        private void UpdateEngine()
        {
            // --- อินพุตของคนขับ (เฉพาะรถที่เราขี่เอง) ---
            Horse? myBike = Game1.player.mount?.modData.ContainsKey(BikeFlagKey) == true ? Game1.player.mount : null;
            if (myBike is not null)
            {
                if (!IsEngineOn(myBike))
                {
                    myBike.modData[EngineOnKey] = "true";
                    myBike.modData[GearKey] = "1";
                }
                else if (this.Config.EngineOffKey.JustPressed())
                {
                    myBike.modData[EngineOnKey] = "false";
                    myBike.modData[RevingKey] = "false";
                }
                else
                {
                    // เกียร์เปลี่ยนอัตโนมัติ (ไม่มีปุ่มเกียร์แล้ว) — เบิ้ลเครื่อง = กดค้าง (sync ให้ทุกคนได้ยิน)
                    myBike.modData[RevingKey] = this.Config.RevKey.IsDown() ? "true" : "false";
                }
            }
            else if (this.Config.EngineOffKey.JustPressed()
                && FindBike(this.LastRiddenBikeId.Value) is Horse parked && IsEngineOn(parked))
            {
                // ลงจากรถแล้วเครื่องยังเดินเบา → กดปุ่มดับได้แม้ไม่ได้นั่งอยู่
                parked.modData[EngineOnKey] = "false";
                parked.modData[RevingKey] = "false";
            }

            // --- เล่นเสียงรถทุกคันที่เครื่องติด (ทั้งที่มีคนขี่ และจอดเดินเบา) + คำนวณความเร็วรถเรา ---
            var active = new HashSet<Guid>();
            Guid? myBikeId = null; bool myMoving = false; int myGear = 1;

            void Process(Horse bike, Farmer? driver)
            {
                if (!bike.modData.ContainsKey(BikeFlagKey) || !IsEngineOn(bike) || !active.Add(bike.HorseId))
                    return;
                bool moving = driver is not null && driver.isMoving();
                bool local = driver?.IsLocalPlayer == true;
                var loc = (driver?.currentLocation ?? bike.currentLocation)?.NameOrUniqueName;
                bool sameMap = loc == Game1.currentLocation?.NameOrUniqueName;
                this.UpdateBikeAudio(bike, moving, sameMap, local);
                if (local)
                {
                    myBikeId = bike.HorseId;
                    myMoving = moving;
                    myGear = GetGear(bike);
                }
            }

            // รถที่มีคนขี่ (ตัวเราหรือคนอื่น)
            foreach (Farmer f in Game1.getOnlineFarmers())
                if (f.mount is Horse bk)
                    Process(bk, f);
            // รถที่จอดในแมพแต่เครื่องยังติด → เดินเบาต่อ (ลงจากรถแล้วเสียงไม่หาย)
            if (Game1.currentLocation is not null)
                foreach (NPC npc in Game1.currentLocation.characters)
                    if (npc is Horse bk)
                        Process(bk, null);

            // ความเร็วรถที่เราขี่ — แปรตามเกียร์ (เกียร์ 1 = ต่ำสุด, เกียร์ 6 = สูงสุด) ไต่นุ่มๆ ผ่าน Buff
            if (myBikeId is not null)
            {
                float t = (myGear - 1) / (float)(MaxGear - 1);
                float target = myMoving ? this.Config.MinSpeed + (this.Config.MaxSpeed - this.Config.MinSpeed) * t : 0f;
                this.CurrentSpeed.Value += (target - this.CurrentSpeed.Value) * 0.06f;
                this.ApplyBikeSpeedBuff(this.CurrentSpeed.Value);
            }
            else
            {
                this.CurrentSpeed.Value = 0f;
            }

            // --- หยุด+ล้างเสียงรถที่ดับเครื่อง/หายไปแล้ว ---
            foreach (Guid id in this.BikeAudios.Keys.ToArray())
            {
                if (!active.Contains(id))
                {
                    BikeAudio dead = this.BikeAudios[id];
                    dead.Loop?.Stop(Microsoft.Xna.Framework.Audio.AudioStopOptions.Immediate);
                    dead.Main?.Stop(Microsoft.Xna.Framework.Audio.AudioStopOptions.Immediate);
                    dead.Rev?.Stop(Microsoft.Xna.Framework.Audio.AudioStopOptions.Immediate);
                    this.BikeAudios.Remove(id);
                }
            }
        }

        /// <summary>pitch เพิ่มของเกียร์ 5-6 (ไม่มีไฟล์เฉพาะ ใช้ไฟล์เกียร์ 4 แล้วดัน pitch ต่อเนื่อง)</summary>
        private static float GearPitch(int gear) => gear switch
        {
            5 => 0.15f,
            6 => 0.30f,
            _ => 0f,
        };

        /// <summary>เล่น/อัปเดตเสียงเครื่องแบบ "เกียร์อัตโนมัติ" — ไล่เสียงเกียร์ 1→6 เอง เสียงจบเปลี่ยนเกียร์เอง</summary>
        private void UpdateBikeAudio(Horse bike, bool movingNow, bool audible, bool isLocalDriver)
        {
            if (!this.BikeAudios.TryGetValue(bike.HorseId, out BikeAudio? a))
            {
                a = new BikeAudio();
                this.BikeAudios[bike.HorseId] = a;
            }

            // เสียงสตาร์ทมือ (ครั้งแรกที่ติดเครื่อง)
            if (!a.Started)
            {
                a.Started = true;
                a.StartDelay = 50;
                this.PlayOneShot(CueStart, bike, audible, 1f);
                return;
            }
            if (a.StartDelay > 0) { a.StartDelay--; return; }

            // debounce การเคลื่อนที่ — isMoving = กดปุ่มเดิน (ติดหิน/หันทิศ = ยังกด = ยัง true)
            if (movingNow)
                a.LastMovingTick = Game1.ticks;
            bool moving = (Game1.ticks - a.LastMovingTick) < 15;

            int gear = GetGear(bike);
            float vol = (audible ? this.VolumeByDistance(bike) : 0f) * this.Config.EngineVolume;

            // fade เสียงเดินเบาที่ค้างอยู่ตอนออกตัว (crossfade idle→เกียร์ 1 ให้สมูธ)
            if (a.FadeLoop is not null)
            {
                a.FadeVol -= 0.06f;
                if (a.FadeVol <= 0f) { a.FadeLoop.Stop(Microsoft.Xna.Framework.Audio.AudioStopOptions.Immediate); a.FadeLoop = null; }
                else a.FadeLoop.Volume = vol * a.FadeVol;
            }

            bool reving = bike.modData.TryGetValue(RevingKey, out string? r) && r == "true";

            // เบิ้ลเครื่อง (กดค้าง): หยุดเสียงเครื่องก่อน แล้วเล่น Burn วนต่อเนื่อง
            if (reving)
            {
                a.Loop?.Stop(Microsoft.Xna.Framework.Audio.AudioStopOptions.Immediate);
                a.Main?.Stop(Microsoft.Xna.Framework.Audio.AudioStopOptions.Immediate);
                a.Loop = null; a.Main = null; a.Sound = ""; a.PlayGear = 0;
                if (a.Rev is null) { a.Rev = Game1.soundBank.GetCue(CueRev); a.Rev.Play(); }
                a.RevFade = 1f;
                a.Rev.Volume = vol;
                return;
            }
            if (a.Rev is not null) // เพิ่งปล่อยเบิ้ล → fade ลงเร็ว
            {
                a.RevFade -= 0.2f;
                if (a.RevFade <= 0f) { a.Rev.Stop(Microsoft.Xna.Framework.Audio.AudioStopOptions.Immediate); a.Rev = null; }
                else a.Rev.Volume = vol * a.RevFade;
            }

            // ---- นิ่ง → เดินเบา + รีเซ็ตเกียร์ 1 ----
            if (!moving)
            {
                if (a.Sound != "idle")
                {
                    a.Main?.Stop(Microsoft.Xna.Framework.Audio.AudioStopOptions.Immediate);
                    a.Loop?.Stop(Microsoft.Xna.Framework.Audio.AudioStopOptions.Immediate);
                    a.Main = null;
                    a.Loop = Game1.soundBank.GetCue(CueIdle);
                    a.Loop.Play();
                    a.Sound = "idle";
                    a.PlayGear = 0;
                }
                if (isLocalDriver && gear != 1)
                    bike.modData[GearKey] = "1";
                if (a.Loop is not null) a.Loop.Volume = vol;
                return;
            }

            // ---- วิ่ง → ไล่เกียร์อัตโนมัติ (เกียร์เจ้าของคือคนขับ ใช้ modData sync) ----
            // เริ่มออกตัวจากเดินเบา → crossfade idle ค้าง fade + เริ่มเกียร์ 1
            if (a.Sound != "gear")
            {
                a.FadeLoop = a.Loop; a.FadeVol = 1f; // ให้เดินเบา fade ต่อ (สมูธ)
                a.Loop = null;
                a.Sound = "gear";
                a.PlayGear = 0;
                if (isLocalDriver) bike.modData[GearKey] = "1";
                gear = 1;
            }

            // คนขับเลื่อนเกียร์อัตโนมัติเมื่อเสียงเกียร์ปัจจุบันเล่นจบ (ยังไม่ถึงเกียร์ 6)
            if (isLocalDriver && gear < MaxGear && a.Main is not null && !a.Main.IsPlaying)
            {
                bike.modData[GearKey] = (gear + 1).ToString();
                gear = gear + 1;
            }

            float pitch = GearPitch(gear);

            // สลับเสียงเมื่อเกียร์เปลี่ยน (ทุกเครื่องทำตาม modData)
            if (a.PlayGear != gear)
            {
                a.PlayGear = gear;
                a.Loop?.Stop(Microsoft.Xna.Framework.Audio.AudioStopOptions.Immediate);
                a.Loop = null;
                a.Main?.Stop(Microsoft.Xna.Framework.Audio.AudioStopOptions.Immediate);
                a.Main = Game1.soundBank.GetCue(this.GearMainCue(gear));
                a.Main.Pitch = pitch;
                a.Main.Play();
            }

            // เกียร์ 6 = เกียร์สุดท้าย เสียงจบแล้ววนต่อเนื่อง (ขับค้างนานๆ)
            if (gear >= MaxGear && a.Main is not null && !a.Main.IsPlaying)
            {
                a.Main = null;
                a.Loop = Game1.soundBank.GetCue(CueGear6Loop);
                a.Loop.Pitch = pitch;
                a.Loop.Play();
            }

            if (a.Main is not null) { a.Main.Pitch = pitch; a.Main.Volume = vol; }
            if (a.Loop is not null) { a.Loop.Pitch = pitch; a.Loop.Volume = vol; }
        }

        /// <summary>เพิ่มความเร็วรถผ่านระบบ Buff (1.6 เปลี่ยน addedSpeed เป็น read-only จาก buff เท่านั้น)</summary>
        private void ApplyBikeSpeedBuff(float speed)
        {
            const string id = "Khaichiaro.BigBike_Speed";
            if (speed <= 0.01f)
            {
                Game1.player.buffs.Remove(id);
                return;
            }
            var fx = new BuffEffects();
            fx.Speed.Value = speed;
            var buff = new Buff(
                id: id,
                source: "Big Bike",
                displaySource: "Big Bike",
                duration: 2000,          // refresh ทุก tick จึงไม่หมดระหว่างขี่
                effects: fx,
                isDebuff: false,
                displayName: "Big Bike")
            {
                visible = false          // ไม่โชว์ไอคอน buff บนจอ
            };
            Game1.player.applyBuff(buff);
        }

        /// <summary>เสียงของเกียร์ (เกียร์ 4-6 ใช้ไฟล์เกียร์ 4 + pitch)</summary>
        private string GearMainCue(int gear) => gear switch
        {
            1 => CueGear1,
            2 => CueGear2,
            3 => CueGear3,
            _ => CueGear4, // 4,5,6
        };

        /// <summary>เล่นเสียงครั้งเดียว (one-shot) ณ ตำแหน่งรถ ดังตามระยะ</summary>
        private void PlayOneShot(string cueId, Horse bike, bool audible, float pitch)
        {
            if (!audible)
                return;
            ICue cue = Game1.soundBank.GetCue(cueId);
            cue.Pitch = pitch;
            cue.Volume = this.VolumeByDistance(bike);
            cue.Play();
        }

        /// <summary>ความดังตามระยะจากผู้เล่นในเครื่องนี้ (ไกลเกิน 14 ช่อง = เงียบ)</summary>
        private float VolumeByDistance(Horse bike)
        {
            float dist = Vector2.Distance(Game1.player.Position, bike.Position);
            return Math.Clamp(1f - dist / (64f * 14f), 0f, 1f);
        }

        // ==================== HUD วงกลมบอกเกียร์ (มุมบนซ้าย) ====================

        /// <summary>รถที่ผู้เล่นเครื่องนี้กำลังนั่งอยู่ (ขับเองหรือซ้อนท้าย) — ไว้แสดง HUD เกียร์</summary>
        private Horse? GetLocalRiddenBike()
        {
            if (Game1.player.mount is Horse m && m.modData.ContainsKey(BikeFlagKey))
                return m;
            if (this.Passengers.TryGetValue(Game1.player.UniqueMultiplayerID, out Guid id))
                return FindBike(id);
            return null;
        }

        /// <summary>สร้าง texture วงกลม pixel-art (ขอบม่วงตามธีม + พื้นในดำโปร่ง)</summary>
        private Texture2D MakeGearHudTexture()
        {
            const int sz = 56;
            var tex = new Texture2D(Game1.graphics.GraphicsDevice, sz, sz);
            var data = new Color[sz * sz];
            float c = sz / 2f, rOut = sz / 2f - 1f, rIn = rOut - 5f;
            Color ring = new Color(124, 58, 237);   // ม่วงตามธีม
            Color fill = new Color(20, 20, 25) * 0.82f;
            for (int y = 0; y < sz; y++)
                for (int x = 0; x < sz; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(c, c));
                    data[y * sz + x] = d <= rIn ? fill : (d <= rOut ? ring : Color.Transparent);
                }
            tex.SetData(data);
            return tex;
        }

        /// <summary>วาด HUD เกียร์ตอนนั่งรถ (คนขับ+คนซ้อนเห็นเหมือนกัน)</summary>
        private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
        {
            if (this.GearHudTexture is null || !Context.IsWorldReady)
                return;
            Horse? bike = this.GetLocalRiddenBike();
            if (bike is null)
                return;

            SpriteBatch b = e.SpriteBatch;
            Vector2 pos = new(32f, 32f);
            b.Draw(this.GearHudTexture, pos, null, Color.White, 0f, Vector2.Zero, 1f, SpriteEffects.None, 1f);

            // เลขเกียร์กลางวงกลม
            string txt = GetGear(bike).ToString();
            SpriteFont font = Game1.dialogueFont;
            Vector2 ts = font.MeasureString(txt);
            Vector2 center = pos + new Vector2(28f, 26f) - ts / 2f;
            b.DrawString(font, txt, center + new Vector2(2f, 2f), Color.Black * 0.6f);
            b.DrawString(font, txt, center, Color.White);

            // ป้าย "GEAR" เล็กๆ ใต้วงกลม
            const string label = "GEAR";
            Vector2 ls = Game1.smallFont.MeasureString(label) * 0.7f;
            Vector2 lp = pos + new Vector2(28f - ls.X / 2f, 56f);
            b.DrawString(Game1.smallFont, label, lp + new Vector2(1f, 1f), Color.Black * 0.5f, 0f, Vector2.Zero, 0.7f, SpriteEffects.None, 1f);
            b.DrawString(Game1.smallFont, label, lp, Color.White, 0f, Vector2.Zero, 0.7f, SpriteEffects.None, 1f);
        }

        /// <summary>หยุดเสียงเครื่องทั้งหมด (ตอนออกจากเซฟ)</summary>
        private void StopAllEngineAudio()
        {
            foreach (BikeAudio a in this.BikeAudios.Values)
            {
                a.Loop?.Stop(Microsoft.Xna.Framework.Audio.AudioStopOptions.Immediate);
                a.Main?.Stop(Microsoft.Xna.Framework.Audio.AudioStopOptions.Immediate);
                a.Rev?.Stop(Microsoft.Xna.Framework.Audio.AudioStopOptions.Immediate);
                a.FadeLoop?.Stop(Microsoft.Xna.Framework.Audio.AudioStopOptions.Immediate);
            }
            this.BikeAudios.Clear();
        }
    }
}
