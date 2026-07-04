using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
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
        /// <summary>id ของสิ่งก่อสร้างโรงรถใน Data/Buildings</summary>
        private const string GarageBuildingId = "Khaichiaro.BigBike_Garage";

        /// <summary>key ใน modData ที่ใช้แท็กว่าม้าตัวนี้คือบิ๊กไบค์</summary>
        private const string BikeFlagKey = "Khaichiaro.BigBike/IsBike";

        /// <summary>ชื่อ asset ของ texture โรงรถ (ผ่าน content pipeline)</summary>
        private const string GarageTextureName = "Mods/Khaichiaro.BigBike/Garage";

        /// <summary>ชื่อ asset ของ texture บิ๊กไบค์</summary>
        private const string BikeTextureName = "Mods/Khaichiaro.BigBike/BigBike";

        /// <summary>ค่าตั้งจาก config.json</summary>
        private ModConfig Config = null!;

        // หมายเหตุ: สถานะ "ต่อผู้เล่น" ต้องใช้ PerScreen — split-screen มีผู้เล่นหลายคนใน process เดียว
        // ถ้าเก็บเป็น field เดียวจะตีกัน (คนขี่ของ screen 1 กับ screen 2 เขียนทับกัน)

        /// <summary>tick ที่แล้วผู้เล่นขี่บิ๊กไบค์อยู่ไหม (ไว้รีเซ็ต offset ตอนลงจากรถ)</summary>
        private readonly PerScreen<bool> WasRidingBike = new();

        /// <summary>รถคันล่าสุดที่เราขับ (ไว้ดีดคนซ้อนตอนเราลงจากรถ)</summary>
        private readonly PerScreen<Guid> LastRiddenBikeId = new();

        /// <summary>tick ที่แล้วเราซ้อนท้ายอยู่ไหม (วาล์วกันค้าง: หลุดจากสถานะซ้อนเมื่อไหร่ ปลดล็อกตัวละครเสมอ)</summary>
        private readonly PerScreen<bool> WasPassenger = new();

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
            else if (e.NameWithoutLocale.IsEquivalentTo("Data/AudioChanges"))
            {
                // ลงทะเบียนเสียงเครื่องยนต์เข้า sound bank ของเกม
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, AudioCueData>().Data;
                    data[EngineStartCue] = new AudioCueData
                    {
                        Id = EngineStartCue,
                        Category = "Sound",
                        FilePaths = new List<string> { Path.Combine(this.Helper.DirectoryPath, "assets", "engine_start.wav") },
                    };
                    data[EngineLoopCue] = new AudioCueData
                    {
                        Id = EngineLoopCue,
                        Category = "Sound",
                        Looped = true,
                        FilePaths = new List<string> { Path.Combine(this.Helper.DirectoryPath, "assets", "engine_loop.wav") },
                    };
                });
            }
            else if (e.NameWithoutLocale.IsEquivalentTo("Data/Buildings"))
            {
                e.Edit(asset =>
                {
                    var data = asset.AsDictionary<string, BuildingData>().Data;
                    data[GarageBuildingId] = new BuildingData
                    {
                        Name = this.Helper.Translation.Get("garage.name"),
                        Description = this.Helper.Translation.Get("garage.description"),
                        Texture = GarageTextureName,
                        Builder = "Robin",
                        BuildCost = this.Config.BuildCost,
                        BuildMaterials = new List<BuildingMaterial>
                        {
                            new() { ItemId = "(O)388", Amount = this.Config.WoodAmount },  // ไม้
                            new() { ItemId = "(O)390", Amount = this.Config.StoneAmount }, // หิน
                        },
                        BuildDays = this.Config.BuildDays,
                        Size = new Point(4, 2),
                        // ใช้คลาส Stable ของเกม → ได้ระบบ spawn/ขี่/ผูกกับผู้เล่นมาฟรีทั้งหมด
                        BuildingType = "StardewValley.Buildings.Stable",
                        HumanDoor = new Point(-1, -1),
                        // ให้อาคารวาดอยู่ "หลัง" ตัวละคร/รถที่ยืนแถวประตู → รถในโรงรถมองเห็นได้
                        SortTileOffset = 1f,
                        // แถวล่าง 2 ช่องกลางเดินทะลุได้ (ช่องประตู) แบบเดียวกับ garage ของ TractorMod
                        CollisionMap = "XXXX\nXOOX",
                    };
                });
            }
        }

        /// <summary>ลงทะเบียนหน้า setting กับ Generic Mod Config Menu (ถ้าผู้เล่นติดตั้งไว้)</summary>
        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
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
                getValue: () => this.Config.WoodAmount,
                setValue: value => this.Config.WoodAmount = value,
                name: () => this.Helper.Translation.Get("config.wood.name"),
                tooltip: () => this.Helper.Translation.Get("config.wood.tooltip"),
                min: 0,
                max: 999
            );
            gmcm.AddNumberOption(
                mod: this.ModManifest,
                getValue: () => this.Config.StoneAmount,
                setValue: value => this.Config.StoneAmount = value,
                name: () => this.Helper.Translation.Get("config.stone.name"),
                tooltip: () => this.Helper.Translation.Get("config.stone.tooltip"),
                min: 0,
                max: 999
            );
            gmcm.AddNumberOption(
                mod: this.ModManifest,
                getValue: () => this.Config.BuildDays,
                setValue: value => this.Config.BuildDays = value,
                name: () => this.Helper.Translation.Get("config.buildDays.name"),
                tooltip: () => this.Helper.Translation.Get("config.buildDays.tooltip"),
                min: 0,
                max: 28
            );
            gmcm.AddKeybindList(
                mod: this.ModManifest,
                getValue: () => this.Config.EngineOffKey,
                setValue: value => this.Config.EngineOffKey = value,
                name: () => this.Helper.Translation.Get("config.engineOff.name"),
                tooltip: () => this.Helper.Translation.Get("config.engineOff.tooltip")
            );

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
                if (building.buildingType.Value == GarageBuildingId && building is Stable garage)
                {
                    Horse? bike = garage.getStableHorse();
                    if (bike is not null)
                    {
                        bike.modData[BikeFlagKey] = "true";
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
                // เราคือคนขับที่เพิ่งลงจากรถ → ประกาศดีดคนซ้อนบนคันนั้นให้ทุกเครื่องรับรู้
                foreach (var pair in this.Passengers.Where(kv => kv.Value == this.LastRiddenBikeId.Value).ToArray())
                    this.SetPassenger(pair.Key, pair.Value, mounted: false, broadcast: true);
            }
            this.WasRidingBike.Value = ridingBike;

            // วาล์วกันค้าง: หลุดจากทะเบียนคนซ้อนเมื่อไหร่ (ไม่ว่าด้วยเหตุไหน) ปลดล็อกตัวละครเสมอ
            bool seated = this.Passengers.ContainsKey(Game1.player.UniqueMultiplayerID);
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
                float distToPlayer = Vector2.Distance(bike.Position, Game1.player.Position);
                float distToCursor = Vector2.Distance(bike.Tile, cursorTile);
                if (distToPlayer <= 64f * 2.5f || distToCursor <= 2f)
                {
                    this.Monitor.Log($"ขึ้นซ้อนท้ายรถของ {other.Name}", LogLevel.Debug);
                    this.SetPassenger(Game1.player.UniqueMultiplayerID, bike.HorseId, mounted: true, broadcast: true);
                    this.Helper.Input.Suppress(e.Button);
                    return;
                }
                this.Monitor.Log($"ซ้อนไม่ได้: ไกลเกิน (ห่างรถ {distToPlayer / 64f:0.0} ช่อง)", LogLevel.Debug);
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

            // อนิเมชันกระโดดขึ้น/ลง — SetPassenger รันทุกเครื่อง (ฝั่ง broadcast + ฝั่งรับ message)
            // เรียก jump บน farmer object โดยตรง จึงเห็นทั้งจอคนขับและจอคนซ้อน ทั้ง split-screen และแยกเครื่อง
            if (changed)
            {
                Farmer? p = Game1.getOnlineFarmers().FirstOrDefault(f => f.UniqueMultiplayerID == playerId);
                p?.jump();
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
                    this.Monitor.Log($"[warp] screen={Context.ScreenId} p={p.Name} local={p.IsLocalPlayer} {pLoc}→{dLoc}", LogLevel.Debug);
                    if (p.IsLocalPlayer)
                        Game1.warpFarmer(driver.currentLocation!.Name, driver.TilePoint.X, driver.TilePoint.Y + 1, false);
                    continue;
                }

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
            this.StopEngine();
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
            Game1.activeClickableMenu = new NamingMenu(
                menu.doneNaming,
                this.Helper.Translation.Get("bike.nameTitle"),
                this.Helper.Translation.Get("bike.defaultName"));
        }

        /// <summary>สลับ sprite sheet ของม้าเป็นบิ๊กไบค์ (เฟรม 32x32 layout เดียวกับม้าเดิม) + ปิดเสียงฝีเท้าม้า</summary>
        private void ApplyBikeSprite(Horse bike)
        {
            if (bike.Sprite?.textureName.Value != BikeTextureName)
                bike.Sprite = new AnimatedSprite(BikeTextureName, 0, 32, 32);
            bike.onFootstepAction = _ => { }; // บิ๊กไบค์ไม่มีกีบเท้า — ใช้เสียงเครื่องยนต์แทน
        }

        // ==================== ระบบเสียงเครื่องยนต์ ====================

        private const string EngineStartCue = "Khaichiaro.BigBike_EngineStart";
        private const string EngineLoopCue = "Khaichiaro.BigBike_EngineLoop";

        // เสียงเครื่องเป็นสถานะ "ต่อผู้เล่น" → PerScreen (split-screen สองคนขี่คนละคัน เสียงคนละตัว)

        /// <summary>ลูปเสียงเครื่องที่กำลังเล่นอยู่ (null = เครื่องดับ/ยังไม่เข้าลูป)</summary>
        private readonly PerScreen<ICue?> EngineCue = new();

        /// <summary>รถคันที่เครื่องยนต์ติดอยู่</summary>
        private readonly PerScreen<Guid> EngineBikeId = new();

        /// <summary>เครื่องยนต์ติดอยู่ไหม (ยังติดต่อแม้ลงจากรถ จนกว่าจะกดปุ่มดับ)</summary>
        private readonly PerScreen<bool> EngineOn = new();

        /// <summary>ตัวนับรอเสียงสตาร์ทจบ ก่อนเริ่มลูปเดินเบา</summary>
        private readonly PerScreen<int> StartCountdown = new();

        /// <summary>pitch ปัจจุบัน (ไต่แบบนุ่มๆ ระหว่างเดินเบา ↔ เร่งรอบ)</summary>
        private readonly PerScreen<float> EnginePitch = new();

        /// <summary>จัดการเสียงเครื่องยนต์ทุก tick: สตาร์ท → ลูป → เร่ง/เดินเบา → ดับด้วยปุ่ม</summary>
        private void UpdateEngine()
        {
            Horse? mountBike = Game1.player.mount?.modData.ContainsKey(BikeFlagKey) == true
                ? Game1.player.mount
                : null;

            // ขึ้นรถตอนเครื่องดับ → สตาร์ทมือ
            if (mountBike is not null && !this.EngineOn.Value)
            {
                this.EngineOn.Value = true;
                this.EngineBikeId.Value = mountBike.HorseId;
                this.EnginePitch.Value = 0f;
                Game1.playSound(EngineStartCue);
                this.StartCountdown.Value = 55; // ~0.9 วิ ให้เสียงสตาร์ทเล่นก่อนเข้าลูป
                return;
            }
            if (!this.EngineOn.Value)
                return;

            // ปุ่มดับเครื่อง (ตั้งได้ทั้งคีย์บอร์ด/จอย)
            if (this.Config.EngineOffKey.JustPressed())
            {
                this.StopEngine();
                return;
            }

            if (this.StartCountdown.Value > 0)
            {
                if (--this.StartCountdown.Value == 0)
                {
                    this.EngineCue.Value = Game1.soundBank.GetCue(EngineLoopCue);
                    this.EngineCue.Value.Play();
                }
                return;
            }
            if (this.EngineCue.Value is null)
                return;

            Horse? bike = mountBike is not null && mountBike.HorseId == this.EngineBikeId.Value
                ? mountBike
                : FindBike(this.EngineBikeId.Value);
            if (bike is null)
            {
                this.StopEngine();
                return;
            }

            // ขี่อยู่+วิ่ง = เร่งรอบ / นอกนั้น = เดินเบา (ไต่ pitch นุ่มๆ)
            bool revving = mountBike == bike && Game1.player.isMoving();
            float target = revving ? 0.55f : 0f;
            this.EnginePitch.Value += (target - this.EnginePitch.Value) * 0.08f;
            this.EngineCue.Value.Pitch = this.EnginePitch.Value;

            // ลงจากรถแล้วเครื่องยังติด → เสียงเบาลงตามระยะห่างจากรถ / เงียบถ้าอยู่คนละแมพ
            if (mountBike == bike)
            {
                this.EngineCue.Value.Volume = 1f;
            }
            else if (bike.currentLocation?.NameOrUniqueName != Game1.currentLocation?.NameOrUniqueName)
            {
                this.EngineCue.Value.Volume = 0f;
            }
            else
            {
                float dist = Vector2.Distance(Game1.player.Position, bike.Position);
                this.EngineCue.Value.Volume = Math.Clamp(1f - dist / (64f * 12f), 0f, 1f);
            }
        }

        /// <summary>ดับเครื่องยนต์</summary>
        private void StopEngine()
        {
            this.EngineOn.Value = false;
            this.EnginePitch.Value = 0f;
            this.EngineCue.Value?.Stop(Microsoft.Xna.Framework.Audio.AudioStopOptions.Immediate);
            this.EngineCue.Value = null;
        }
    }
}
