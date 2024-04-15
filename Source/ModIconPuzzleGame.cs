using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using UnityEngine;
using HarmonyLib;
using System.Reflection.Emit;
using static System.Net.Mime.MediaTypeNames;
using static UnityEngine.Scripting.GarbageCollector;
using DelaunatorSharp;
using System;

namespace ModIconPuzzleGame
{
	public class Mod : Verse.Mod
	{
		public Mod(ModContentPack content) : base(content)
		{
#if DEBUG
			Harmony.DEBUG = true;
#endif

			Harmony harmony = new Harmony("Uuugggg.rimworld.ModIconPuzzleGame.main");
			harmony.PatchAll();
		}
	}



	public struct PuzzleCache
	{
		static object modSliding;

		private Dictionary<object, float> angleForIcon;
		private Dictionary<object, Vector2> slideForIcon;

		List<(float, Rect, Texture)> toDraw;

		public PuzzleCache()
		{
			angleForIcon = [];
			slideForIcon = [];

			toDraw = [];
		}

		public float AngleForIcon(object icon)
		{
			if (angleForIcon.TryGetValue(icon, out float angle))
				return angle;
			return default;
		}
		public void SetAngleForIcon(object icon, float angle)
		{
			angleForIcon[icon] = angle;
		}
		public void AddAngleForIcon(object icon, float angleToAdd)
		{
			angleForIcon[icon] = (AngleForIcon(icon) + angleToAdd) % 360;
		}

		public Vector2 SlideForIcon(object icon)
		{
			if (slideForIcon.TryGetValue(icon, out Vector2 slide))
				return slide;
			return default;
		}
		public void SetSlideForIcon(object icon, Vector2 slide)
		{
			slideForIcon[icon] = slide;
		}
		public void AddSlideForIcon(object icon, Vector2 slideToAdd)
		{
			slideForIcon[icon] = SlideForIcon(icon) + slideToAdd;
		}

		public void Draw(Rect rect, Texture tex, object mod)
		{
			Vector2 slide = SlideForIcon(mod);
			rect.position += slide;

			if (modSliding != null)
			{
				if (Event.current.type == EventType.MouseUp)
				{
					modSliding = null;
					Event.current.Use();
				}
				else if (Event.current.type == EventType.MouseDrag)
				{
					AddSlideForIcon(modSliding, Event.current.delta);
					Event.current.Use();
				}
			}

			if (Event.current.type == EventType.MouseDown && Mouse.IsOver(rect))
			{
				if (Event.current.button == 1)
				{
					AddAngleForIcon(mod, 90);
				}
				else
				{
					modSliding = mod;
				}
				Event.current.Use();
			}

			//Widgets.DrawTextureFitted(outerRect, tex, scale, new Vector2(tex.width, tex.height), new Rect(0f, 0f, 1f, 1f), angle);

			if (Event.current.type == EventType.Repaint)
			{
				float angle = AngleForIcon(mod);
				toDraw.Add((angle, rect, tex));
			}
		}
		public void PostfixDraw()
		{
			foreach ((float angle, Rect rect, Texture tex) in toDraw)
			{
				if (angle != 0f)
				{
					Matrix4x4 matrix = GUI.matrix;
					UI.RotateAroundPivot(angle, rect.center);
					GUI.DrawTexture(rect, tex);
					GUI.matrix = matrix;
				}
				else
					GUI.DrawTexture(rect, tex);
			}
			toDraw.Clear();
		}
	}

	[StaticConstructorOnStartup]
	[HarmonyPatch(typeof(Dialog_Options), nameof(Dialog_Options.DoModOptions))]
	public static class MoveModIconSettings
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo DrawOptionBackgroundInfo = AccessTools.Method(typeof(Widgets), nameof(Widgets.DrawOptionBackground));

			MethodInfo NullOrBadInfo = AccessTools.Method(typeof(BaseContent), nameof(BaseContent.NullOrBad), [typeof(Texture2D)]);

			MethodInfo DrawTextureFittedInfo = AccessTools.Method(typeof(GUI), nameof(GUI.DrawTexture),
				[typeof(Rect), typeof(Texture)]);

			List<CodeInstruction> insts = instructions.ToList();
			for (int i = 0; i < insts.Count; i++)
			{
				var inst = insts[i];


				yield return inst;
				if (inst.Calls(DrawOptionBackgroundInfo))
				{
					// The options button that the icon draws over of course takes input
					// So need to move the button block after the drawtex block
					// They're both in if statements after DrawOptionBackground

					// So, move the 8 lines for ButtonInvisible, to after the DrawTextureFitted
					// Have to swap labels and branches around because if blocks reordered

					CodeInstruction branchButtonInvisible, branchIconNull;
					Label labelAfterAll, labelAfterButton;

					// Hardcode that it's 8 lines to handle the ButtonInvisible block
					int buttonI = i + 1;
					int buttonEnd = i + 8;

					// Swap the labels in the operands (for branching out of the if statemetns)
					branchButtonInvisible = insts[i + 4];
					labelAfterButton = (Label)branchButtonInvisible.operand;//i+9

					int iconNullOrBadI = insts.FindIndex(ci => ci.Calls(NullOrBadInfo)) + 1;
					branchIconNull = insts[iconNullOrBadI];
					labelAfterAll = (Label)branchIconNull.operand;

					branchButtonInvisible.operand = labelAfterAll;
					branchIconNull.operand = labelAfterButton;

					// set label of mid if-branch
					insts[buttonEnd + 1].labels.Clear();  // labelAfterButton - this is going to be the next line and doesn't need a label

					insts[buttonI].labels.Add(labelAfterButton); // this will come after an if block and gets jumped to, from branchIconNull


					CodeInstruction ldMod = insts[i + 6];

					// Write out-of-order lines

					// A little more clever with this:
					int drawTexI = buttonEnd;


					// Yield tex lines, starting after the button block, ending once it hits the label that is after all this.
					while (!insts[drawTexI + 1].labels.Contains(labelAfterAll))
					{
						drawTexI++;
						if (insts[drawTexI].Calls(DrawTextureFittedInfo))
						{
							yield return ldMod;

							// Here's what the transpiler should be, just to insert OverrideDrawTextureFitted.
							// Simple.
							// But that damn button had to block input, so all the above had to be written.
							yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MoveModIconSettings), nameof(OverrideDrawTextureFitted)));
						}
						else
							yield return insts[drawTexI];
					}

					// Yield button insts, 8 of em.
					for (int bi = buttonI; bi <= buttonEnd; bi++)
						yield return insts[bi];

					i = drawTexI;
				}
			}
		}



		static PuzzleCache puz = new();
		public static void OverrideDrawTextureFitted(Rect rect, Texture tex, Mod mod)
		{
			puz.Draw(rect, tex, mod);
		}

		public static void Postfix()
		{
			puz.PostfixDraw();
		}
	}


	[HarmonyPatch(typeof(ModSummaryWindow))]// This is a delegate INSIDE OF: nameof(ModSummaryWindow.DrawContents))]
	public static class MoveModIconSummary
	{
		public static MethodInfo TargetMethod() =>
			typeof(ModSummaryWindow)
			.GetNestedTypes(AccessTools.all)
			.First(
				t => t.GetFields(AccessTools.all)
				.Any(f => f.FieldType == typeof(ModMetaData)))
			.GetMethods(AccessTools.all)
			.First(
				m => m.GetParameters().Length == 1
				&& m.GetParameters()[0].ParameterType == typeof(Rect));

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo DrawTextureInfo = AccessTools.Method(typeof(GUI), nameof(GUI.DrawTexture),
				 [typeof(Rect), typeof(Texture)]);

			MethodInfo IconGetterInfo = AccessTools.PropertyGetter(typeof(ModMetaData), nameof(ModMetaData.Icon));

			MethodInfo ButtonInvisibleInfo = AccessTools.Method(typeof(Widgets), nameof(Widgets.ButtonInvisible));

			bool firstIcon = false;

			foreach (var inst in instructions)
			{
				if (inst.Calls(IconGetterInfo))
				{
					if (!firstIcon)
						firstIcon = true;
					else
						continue; //ModMetaData, not ModMetaData.Icon
				}

				if(inst.Calls(ButtonInvisibleInfo))
				{
					yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MoveModIconSummary), nameof(ButtonInvisibleButNotIcon)));
				}
				else if (inst.Calls(DrawTextureInfo))
				{
					//DrawTextureForMod(Rect , ModMetaData )
					yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(MoveModIconSummary), nameof(DrawTextureForMod)));
				}
				else
					yield return inst;
			}
		}

		public static PuzzleCache puz = new();
		public static void DrawTextureForMod(Rect rect, ModMetaData mod)
		{
			Texture tex = mod.Icon;
			puz.Draw(rect, tex, mod);
		}

		//public static bool ButtonInvisible(Rect butRect, bool doMouseoverSound = true)
		public static bool ButtonInvisibleButNotIcon(Rect butRect, bool doMouseoverSound = true)
		{
			Rect iconRect = new Rect(butRect.x + 8f, butRect.y + 2f, 32f, 32f);
			if (Mouse.IsOver(iconRect))
				return false;

			return Widgets.ButtonInvisible(butRect, doMouseoverSound);
		}
	}


	[HarmonyPatch(typeof(ModSummaryWindow), nameof(ModSummaryWindow.DrawContents))]
	public static class ModSummaryWindowPostfix
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo EndScrollViewInfo = AccessTools.Method(typeof(Widgets), nameof(Widgets.EndScrollView));

			foreach (var inst in instructions)
			{
				if(inst.Calls(EndScrollViewInfo))
				{
					yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ModSummaryWindowPostfix), nameof(PostfixBeforeScrollEnd)));
				}
				
				yield return inst;
			}
		}

		public static void PostfixBeforeScrollEnd()
		{
			MoveModIconSummary.puz.PostfixDraw();
		}
	}
}