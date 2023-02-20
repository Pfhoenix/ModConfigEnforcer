using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace MCE_Test
{
	public class RecipeStub
	{
		public class RequirementStub
		{
			public string Component;
			public int Amount;
			public int AmountPerLevel;
			public bool Recoverable = true;

			RequirementStub() { }

			public static RequirementStub Parse(string def)
			{
				string[] vals = def.Split(':');
				if (vals.Length < 2) return null;
				RequirementStub rs = new RequirementStub();
				rs.Component = vals[0];
				int.TryParse(vals[1], out rs.Amount);
				if (vals.Length > 2) int.TryParse(vals[2], out rs.AmountPerLevel);

				return rs;
			}
		}
		public string Name => "Recipe" + Item.name;
		public ItemDrop Item;
		public string CraftingStation;
		public string RepairStation;
		public int MinStationLevel = 1;
		public List<RequirementStub> Requirements = new List<RequirementStub>();
	}

	[HarmonyPatch]
	public static class AssetHelper
	{
		public static readonly List<GameObject> Prefabs = new List<GameObject>();
		static readonly List<RecipeStub> RecipeStubs = new List<RecipeStub>();
		static readonly List<KeyValuePair<string, StatusEffect>> StatusEffects = new List<KeyValuePair<string, StatusEffect>>();

		public static AssetBundle LoadAssetBundle(string name)
		{
			Assembly assembly = Assembly.GetCallingAssembly();
			AssetBundle ab = AssetBundle.LoadFromStream(assembly.GetManifestResourceStream($"{assembly.GetName().Name}.{name}"));
#if DEBUG
			foreach (var s in assembly.GetManifestResourceNames()) Plugin.Log.LogInfo("Resource: " + s);
			foreach (var s in ab.GetAllAssetNames()) Plugin.Log.LogInfo("Asset in bundle: " + s);
#endif

			return ab;
		}

		public static void RegisterPrefab(GameObject prefab)
		{
			Prefabs.Add(prefab);
			GameObject.DontDestroyOnLoad(prefab);
		}

		public static void RegisterRecipe(RecipeStub rs)
		{
			RecipeStubs.Add(rs);
		}

		public static void RegisterStatusEffect<T>(string name) where T : StatusEffect
		{
			StatusEffects.Add(new KeyValuePair<string, StatusEffect>(name, ScriptableObject.CreateInstance<T>()));
		}

		public static RecipeStub GetRecipeStub(string itemName)
		{
			return RecipeStubs.Find(r => r.Item.name == itemName);
		}

		public static Recipe BuildRecipe(RecipeStub rs, ObjectDB odb)
		{
			var recipe = ScriptableObject.CreateInstance<Recipe>();
			recipe.m_item = rs.Item;
			recipe.m_craftingStation = odb.GetItemPrefab(rs.CraftingStation)?.GetComponentInChildren<CraftingStation>(true);
			if (recipe.m_craftingStation == null)
			{
				recipe.m_craftingStation = ZNetScene.instance?.GetPrefab(rs.CraftingStation)?.GetComponentInChildren<CraftingStation>(true);
				if (recipe.m_craftingStation == null)
				{
					Plugin.Log.LogInfo("BuildRecipe couldn't find crafting station " + rs.CraftingStation + " for " + rs.Name);
					return null;
				}
			}
			if (!string.IsNullOrWhiteSpace(rs.RepairStation))
			{
				recipe.m_repairStation = odb.GetItemPrefab(rs.RepairStation)?.GetComponentInChildren<CraftingStation>(true);
				if (recipe.m_repairStation == null)
				{
					recipe.m_repairStation = ZNetScene.instance?.GetPrefab(rs.RepairStation)?.GetComponentInChildren<CraftingStation>(true);
					if (recipe.m_repairStation == null)
					{
						Plugin.Log.LogInfo("BuildRecipe couldn't find declared repair station " + rs.RepairStation + " for " + rs.Name);
						return null;
					}
				}
				if (recipe.m_repairStation == null) return null;
			}
			recipe.m_minStationLevel = Mathf.Max(1, rs.MinStationLevel);
			List<Piece.Requirement> reqs = new List<Piece.Requirement>();
			foreach (RecipeStub.RequirementStub r in rs.Requirements)
			{
				ItemDrop id = odb.GetItemPrefab(r.Component)?.GetComponentInChildren<ItemDrop>(true);
				if (!id)
				{
					Plugin.Log.LogInfo("BuildRecipe couldn't get requirement component " + r.Component + " for " + rs.Name);
					return null;
				}
				Piece.Requirement pr = new Piece.Requirement();
				pr.m_resItem = id;
				pr.m_amount = r.Amount;
				pr.m_amountPerLevel = r.AmountPerLevel;
				pr.m_recover = r.Recoverable;
				reqs.Add(pr);
			}
			recipe.m_resources = reqs.ToArray();

			return recipe;
		}

		static void PopulateObjectDB(ObjectDB odb)
		{
			if (Prefabs.Count > 0 && !odb.m_items.Find(p => p.name == Prefabs[0].name))
			{
				foreach (var p in Prefabs)
					if (p.GetComponentInChildren<ItemDrop>(true)) odb.m_items.Add(p);

				odb.UpdateItemHashes();
			}

			if (RecipeStubs.Count > 0 && !odb.GetRecipe(RecipeStubs[0].Item.m_itemData))
			{
				foreach (var rs in RecipeStubs)
				{
					Recipe r = BuildRecipe(rs, odb);
					if (r)
					{
						odb.m_recipes.Add(r);
						Plugin.Log.LogInfo("Added recipe " + r.name);
					}
				}
			}

			if (StatusEffects.Count > 0 && !odb.GetStatusEffect(StatusEffects[0].Key))
			{
				foreach (var se in StatusEffects)
				{
					odb.m_StatusEffects.Add(se.Value);
				}
			}
		}

		public static void UpdateRecipes()
		{
			foreach (var rs in RecipeStubs)
			{
				Recipe r = BuildRecipe(rs, ObjectDB.instance);
				if (r)
				{
					var tr = ObjectDB.instance.m_recipes.Find(rt => rt.name == r.name);
					if (tr) ObjectDB.instance.m_recipes.Remove(tr);
					ObjectDB.instance.m_recipes.Add(r);
					Plugin.Log.LogInfo("Updated recipe " + r.name);
				}
			}
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(ObjectDB), "Awake")]
		public static void AwakePostfix(ObjectDB __instance)
		{
			PopulateObjectDB(__instance);
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
		public static void CopyOtherDBPostfix(ObjectDB __instance, ObjectDB other)
		{
			PopulateObjectDB(__instance);
		}

		[HarmonyPostfix]
		[HarmonyPatch(typeof(ZNetScene), "Awake")]
		public static void AwakePostfix(ZNetScene __instance)
		{
			if (Prefabs.Count == 0) return;

			foreach (var p in Prefabs)
				__instance.m_namedPrefabs[p.name.GetStableHashCode()] = p;
		}

		/*[HarmonyPatch(typeof(UnityEngine.Object), "Destroy", new Type[] { typeof(UnityEngine.Object) })]
		public class ObjectPatches
		{
			public static void Prefix(UnityEngine.Object obj)
			{
				if (obj == Plugin.OdinWingsPrefab)
				{
					System.Diagnostics.StackTrace st = new System.Diagnostics.StackTrace();
					Plugin.Log.LogInfo(st.ToString());
				}
				//Plugin.Log.LogInfo("Destroying " + obj.name);
			}
		}*/
	}
}
