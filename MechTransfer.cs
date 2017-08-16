using MechTransfer.ContainerAdapters;
using MechTransfer.Items;
using MechTransfer.Tiles;
using System;
using System.Collections.Generic;
using System.IO;
using Terraria;
using Terraria.DataStructures;
using Terraria.ID;
using Terraria.ModLoader;
using System.Reflection;
using System.Linq.Expressions;
using EnumerateItemsDelegate = System.Func<int, int, System.Collections.Generic.IEnumerable<System.Tuple<Terraria.Item, object>>>;
using InjectItemDelegate = System.Func<int, int, Terraria.Item, bool>;
using TakeItemDelegate = System.Action<int, int, object, int>;

namespace MechTransfer
{
    public class MechTransfer : Mod
    {
        public enum ModMessageID { FilterSyncing, CreateDust }

        internal Dictionary<int, ContainerAdapterDefinition> ContainerAdapters = new Dictionary<int, ContainerAdapterDefinition>();
        internal HashSet<int> PickupBlacklist = new HashSet<int>();
        private const string callErorPrefix = "MechTransfer Call() error: ";
        private const string registerAdapter = "RegisterAdapter";
        private const string registerAdapterReflection = "RegisterAdapterReflection";

        public MechTransfer()
        {
            Properties = new ModProperties()
            {
                Autoload = true,
                AutoloadGores = true,
                AutoloadSounds = true
            };
            TransferUtils.mod = this;
        }

        public override object Call(params object[] args)
        {
            if ((args[0] as string) == registerAdapter)
            {
                if (args.Length != 5)
                {
                    ErrorLogger.Log(callErorPrefix + "Invalid number of arguments at " + registerAdapter);
                    return null;
                }

                ContainerAdapterDefinition definition = new ContainerAdapterDefinition();

                if (!(args[1] is InjectItemDelegate))
                {
                    ErrorLogger.Log(callErorPrefix + "Invalid argument 2 InjectItem at " + registerAdapter);
                    return null;
                }

                definition.InjectItem = args[1] as InjectItemDelegate;

                if (!(args[2] is EnumerateItemsDelegate))
                {
                    ErrorLogger.Log(callErorPrefix + "Invalid argument 3 EnumerateItems at " + registerAdapter);
                    return null;
                }

                definition.EnumerateItems = args[2] as EnumerateItemsDelegate;

                if (!(args[3] is TakeItemDelegate))
                {
                    ErrorLogger.Log(callErorPrefix + "Invalid argument 4 TakeItem at " + registerAdapter);
                    return null;
                }

                definition.TakeItem = args[3] as TakeItemDelegate;

                if (!(args[4] is int[]))
                {
                    ErrorLogger.Log(callErorPrefix + "Invalid argument 5 TileType at " + registerAdapter);
                    return null;
                }

                foreach (var type in (int[])args[4])
                {
                    if (!ContainerAdapters.ContainsKey(type))
                        ContainerAdapters.Add(type, definition);
                }
                return definition;
            }
            if ((args[0] as string) == registerAdapterReflection)
            {
                try
                {
                    ContainerAdapterDefinition definition = new ContainerAdapterDefinition();

                    Type type = args[1].GetType();

                    ParameterExpression paramX = Expression.Parameter(typeof(int));
                    ParameterExpression paramY = Expression.Parameter(typeof(int));

                    MethodInfo inject = type.GetMethod("InjectItem", new Type[] { typeof(int), typeof(int), typeof(Item) });
                    ParameterExpression paramInjectItem = Expression.Parameter(typeof(Item));
                    InjectItemDelegate injectLambda = Expression.Lambda<InjectItemDelegate>(
                        Expression.Call(Expression.Constant(args[1]), inject, paramX, paramY, paramInjectItem),
                        paramX, paramY, paramInjectItem).Compile();

                    MethodInfo enumerate = type.GetMethod("EnumerateItems", new Type[] { typeof(int), typeof(int) });
                    EnumerateItemsDelegate enumerateLambda = Expression.Lambda<EnumerateItemsDelegate>(
                        Expression.Call(Expression.Constant(args[1]), enumerate, paramX, paramY),
                        paramX, paramY).Compile();

                    MethodInfo take = type.GetMethod("TakeItem", new Type[] { typeof(int), typeof(int), typeof(object), typeof(int) });
                    ParameterExpression paramTakeIdentifier = Expression.Parameter(typeof(object));
                    ParameterExpression paramTakeAmount = Expression.Parameter(typeof(int));
                    TakeItemDelegate takeLambda = Expression.Lambda<TakeItemDelegate>(
                        Expression.Call(Expression.Constant(args[1]), take, paramX, paramY, paramTakeIdentifier, paramTakeAmount),
                        paramX, paramY, paramTakeIdentifier, paramTakeAmount).Compile();

                    definition.InjectItem = injectLambda;
                    definition.EnumerateItems = enumerateLambda;
                    definition.TakeItem = takeLambda;

                    if (!(args[2] is int[]))
                    {
                        ErrorLogger.Log(callErorPrefix + "Invalid argument 5 TileType at " + registerAdapterReflection);
                        return null;
                    }

                    foreach (var t in (int[])args[2])
                    {
                        if(!ContainerAdapters.ContainsKey(t))
                            ContainerAdapters.Add(t, definition);
                    }
                    return definition;
                }
                catch (Exception e)
                {
                    ErrorLogger.Log(callErorPrefix + "An exception has occurred while loading adapter at " + registerAdapterReflection);
                    ErrorLogger.Log(e.Message);
                    return null;
                }
            }
            ErrorLogger.Log(callErorPrefix + "Invalid command");
            return null;
        }

        public override bool HijackGetData(ref byte messageType, ref BinaryReader reader, int playerNumber)
        {
            if (Main.netMode == 1 && messageType == MessageID.SyncChestItem && Main.LocalPlayer.chest == reader.ReadInt16())
            {
                Recipe.FindRecipes();
            }
            return false;
        }

        public override void HandlePacket(BinaryReader reader, int whoAmI)
        {
            ModMessageID id = (ModMessageID)reader.ReadInt32();
            if (id == ModMessageID.FilterSyncing)
            {
                if (Main.netMode != 2)
                    return;

                TransferFilterTileEntity entity = (TransferFilterTileEntity)TileEntity.ByID[reader.ReadInt32()];
                entity.ItemId = reader.ReadInt32();
                NetMessage.SendData(MessageID.TileEntitySharing, -1, whoAmI, null, entity.ID, entity.Position.X, entity.Position.Y);
            }
            else if(id == ModMessageID.CreateDust)
            {
                if (Main.netMode != 1)
                    return;

                Dust.NewDustPerfect(reader.ReadVector2(), DustID.Silver, reader.ReadVector2()).noGravity = true;
            }
        }

        public override void Load()
        {
            LoadItems();
        }

        public override void PostSetupContent()
        {
            LoadAdapters();
            LoadBlacklist();
        }

        private void LoadAdapters()
        {
            //Item frame
            ItemFrameAdapter itemFrameAdapter = new ItemFrameAdapter();
            Call(registerAdapterReflection, itemFrameAdapter, new int[] { TileID.ItemFrame });

            //Snowball launcher
            SnowballLauncherAdapter snowballLauncherAdapter = new SnowballLauncherAdapter();
            Call(registerAdapterReflection, snowballLauncherAdapter, new int[] { TileID.SnowballLauncher });

            //Cannon
            CannonAdapter cannonAdapter = new CannonAdapter();
            Call(registerAdapterReflection, cannonAdapter, new int[] { TileID.Cannon });

            //Crystal stand
            CrystalStandAdapter crystalStandAdapter = new CrystalStandAdapter();
            Call(registerAdapterReflection, crystalStandAdapter, new int[] { TileID.ElderCrystalStand });

            //Weapon rack
            WeaponRackAdapter weaponRackAdapter = new WeaponRackAdapter();
            Call(registerAdapterReflection, weaponRackAdapter, new int[] { TileID.WeaponsRack });

            //Chest
            ChestAdapter chestAdapter = new ChestAdapter();
            List<int> chestTypes = new List<int>();
            for (int i = 0; i < TileLoader.TileCount; i++)
            {
                if (TileID.Sets.BasicChest[i] || TileID.Sets.BasicChestFake[i])
                {
                    chestTypes.Add(i);
                    continue;
                }
            }
            Call(registerAdapterReflection, chestAdapter, chestTypes.ToArray());
        }

        public override void AddRecipes()
        {
            //Assembler
            ModRecipe r = new ModRecipe(this);
            r.AddIngredient(ItemType<PneumaticActuatorItem>(), 1);
            r.AddIngredient(ItemID.Cog, 10);
            r.AddTile(TileID.WorkBenches);
            r.SetResult(ItemType("TransferAssemblerItem"), 1);
            r.AddRecipe();

            //Extractor
            r = new ModRecipe(this);
            r.AddIngredient(ItemType<PneumaticActuatorItem>(), 1);
            r.AddIngredient(ItemID.GoldenKey, 1);
            r.AddIngredient(ItemID.Wire, 2);
            r.SetResult(ItemType("TransferExtractorItem"), 1);
            r.AddTile(TileID.WorkBenches);
            r.AddRecipe();

            //Injector
            r = new ModRecipe(this);
            r.AddIngredient(ItemType<PneumaticActuatorItem>(), 1);
            r.AddIngredient(ItemID.GoldenKey, 1);
            r.AddIngredient(ItemID.Wire, 2);
            r.SetResult(ItemType("TransferInjectorItem"), 1);
            r.AddTile(TileID.WorkBenches);
            r.AddRecipe();

            //Filter
            r = new ModRecipe(this);
            r.AddIngredient(ItemType<PneumaticActuatorItem>(), 1);
            r.AddIngredient(ItemID.Actuator, 1);
            r.AddIngredient(ItemID.ItemFrame, 1);
            r.AddTile(TileID.WorkBenches);
            r.SetResult(ItemType("TransferFilterItem"), 1);
            r.AddRecipe();

            //Gate
            r = new ModRecipe(this);
            r.AddIngredient(ItemType<PneumaticActuatorItem>(), 1);
            r.AddIngredient(ItemID.Actuator, 1);
            r.AddTile(TileID.WorkBenches);
            r.SetResult(ItemType("TransferGateItem"), 1);
            r.AddRecipe();

            //Inlet
            r = new ModRecipe(this);
            r.AddIngredient(ItemType<PneumaticActuatorItem>(), 1);
            r.AddIngredient(ItemID.InletPump, 1);
            r.SetResult(ItemType("TransferInletItem"), 1);
            r.AddTile(TileID.WorkBenches);
            r.AddRecipe();

            //Outlet
            r = new ModRecipe(this);
            r.AddIngredient(ItemType<PneumaticActuatorItem>(), 1);
            r.AddIngredient(ItemID.OutletPump, 1);
            r.SetResult(ItemType("TransferOutletItem"), 1);
            r.AddTile(TileID.WorkBenches);
            r.AddRecipe();

            //Pipe
            r = new ModRecipe(this);
            r.AddIngredient(ItemType<PneumaticActuatorItem>(), 1);
            r.AddIngredient(ItemID.IronBar, 1);
            r.anyIronBar = true;
            r.AddTile(TileID.Anvils);
            r.SetResult(ItemType("TransferPipeItem"), 25);
            r.AddRecipe();

            //Relay
            r = new ModRecipe(this);
            r.AddIngredient(ItemType<PneumaticActuatorItem>(), 1);
            r.AddIngredient(ItemID.RedPressurePlate, 1);
            r.anyPressurePlate = true;
            r.AddTile(TileID.WorkBenches);
            r.SetResult(ItemType("TransferRelayItem"), 1);
            r.AddRecipe();
        }

        private void LoadItems()
        {
            //Assembler
            SimplePlaceableItem i = new SimplePlaceableItem();
            i.placeType = TileType<TransferAssemblerTile>();
            AddItem("TransferAssemblerItem", i);
            i.DisplayName.AddTranslation(LangID.English, "Transfer assembler");
            i.Tooltip.AddTranslation(LangID.English, "WIP\nCrafts items automatically\nRight click with item in hand to set filter");

            //Extractor
            SimplePlaceableItem j = new SimplePlaceableItem();
            j.placeType = TileType<TransferExtractorTile>();
            AddItem("TransferExtractorItem", j);
            j.DisplayName.AddTranslation(LangID.English, "Transfer extractor");
            j.Tooltip.AddTranslation(LangID.English, "Extracts items from adjacent chests");

            //Injector
            i = new SimplePlaceableItem();
            i.placeType = TileType<TransferInjectorTile>();
            AddItem("TransferInjectorItem", i);
            i.DisplayName.AddTranslation(LangID.English, "Transfer Injector");
            i.Tooltip.AddTranslation(LangID.English, "Injects items into adjacent chests");

            //Filter
            i = new SimplePlaceableItem();
            i.placeType = TileType<TransferFilterTile>();
            AddItem("TransferFilterItem", i);
            i.DisplayName.AddTranslation(LangID.English, "Transfer filter");
            i.Tooltip.AddTranslation(LangID.English, "Place in line with Transfer pipe\nRight click with item in hand to set filter");

            //Gate
            i = new SimplePlaceableItem();
            i.placeType = TileType<TransferGateTile>();
            AddItem("TransferGateItem", i);
            i.DisplayName.AddTranslation(LangID.English, "Transfer gate");
            i.Tooltip.AddTranslation(LangID.English, "Place in line with Transfer pipe to toggle the item flow with wire");

            //Inlet
            i = new SimplePlaceableItem();
            i.placeType = TileType<TransferInletTile>();
            AddItem("TransferInletItem", i);
            i.DisplayName.AddTranslation(LangID.English, "Transfer inlet");
            i.Tooltip.AddTranslation(LangID.English, "Picks up dropped items");

            //Outlet
            i = new SimplePlaceableItem();
            i.placeType = TileType<TransferOutletTile>();
            AddItem("TransferOutletItem", i);
            i.DisplayName.AddTranslation(LangID.English, "Transfer outlet");
            i.Tooltip.AddTranslation(LangID.English, "Drops item");

            //Pipe
            i = new SimplePlaceableItem();
            i.placeType = TileType<TransferPipeTile>();
            AddItem("TransferPipeItem", i);
            i.DisplayName.AddTranslation(LangID.English, "Transfer pipe");
            i.Tooltip.AddTranslation(LangID.English, "Used to connect item transfer devices");

            //Relay
            i = new SimplePlaceableItem();
            i.placeType = TileType<TransferRelayTile>();
            AddItem("TransferRelayItem", i);
            i.DisplayName.AddTranslation(LangID.English, "Transfer relay");
            i.Tooltip.AddTranslation(LangID.English, "Receives items, and sends them out again");
        }

        private void LoadBlacklist()
        {
            PickupBlacklist.Add(ItemID.Heart);
            PickupBlacklist.Add(ItemID.CandyApple);
            PickupBlacklist.Add(ItemID.CandyCane);

            PickupBlacklist.Add(ItemID.Star);
            PickupBlacklist.Add(ItemID.SugarPlum);
            PickupBlacklist.Add(ItemID.SoulCake);

            PickupBlacklist.Add(ItemID.NebulaPickup1);
            PickupBlacklist.Add(ItemID.NebulaPickup2);
            PickupBlacklist.Add(ItemID.NebulaPickup3);

            PickupBlacklist.Add(ItemID.DD2EnergyCrystal);

            for (int i = 0; i < ItemLoader.ItemCount; i++)
            {
                ModItem item = ItemLoader.GetItem(i);
                if (item != null &&
                   (item.GetType().GetMethod("ItemSpace").DeclaringType != typeof(ModItem) ||
                   item.GetType().GetMethod("OnPickup").DeclaringType != typeof(ModItem)))
                {
                    PickupBlacklist.Add(item.item.type);
                }
            }
        }
    }
}