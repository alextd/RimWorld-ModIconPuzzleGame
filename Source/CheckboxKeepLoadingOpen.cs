using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using Verse;
using HarmonyLib;
using System.Reflection;
using UnityEngine;
using RimWorld;
using Verse.Noise;

namespace ModIconPuzzleGame
{

	[HarmonyPatch(typeof(ModSummaryWindow), nameof(ModSummaryWindow.DrawContents))]
	public static class CheckboxKeepLoadingOpen
	{
		public static bool keepAlive;

		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
			Transpilers.MethodReplacer(instructions,
				AccessTools.Method(typeof(Widgets), nameof(Widgets.BeginScrollView)),
				AccessTools.Method(typeof(CheckboxKeepLoadingOpen), nameof(BeginScrollViewAndDrawThisThing)));

		//public static void BeginScrollView(Rect outRect, ref Vector2 scrollPosition, Rect viewRect, bool showScrollbars = true)
		public static void BeginScrollViewAndDrawThisThing(Rect outRect, ref Vector2 scrollPosition, Rect viewRect, bool showScrollbars = true)
		{
			Rect belowRect = new Rect(((float)UI.screenWidth - LongEventHandler.StatusRectSize.x) / 2f, outRect.yMax + GenUI.Gap * 2,
				LongEventHandler.StatusRectSize.x, LongEventHandler.StatusRectSize.y);


			Widgets.DrawShadowAround(belowRect);
			Widgets.DrawWindowBackground(belowRect);

			Rect labelRect = belowRect;
			labelRect.xMin += GenUI.Gap;
			labelRect.xMax -= GenUI.Gap;
			Widgets.CheckboxLabeled(labelRect, "Keep Puzzling", ref keepAlive);


			Widgets.BeginScrollView(outRect, ref scrollPosition, viewRect, showScrollbars);
		}
	}


	[HarmonyPatch(typeof(LongEventHandler), nameof(LongEventHandler.UpdateCurrentAsynchronousEvent))]
	public static class UpdateCurrentAsynchronousEventPatch
	{
		public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
			Transpilers.MethodReplacer(instructions,
				AccessTools.PropertyGetter(typeof(Thread), nameof(Thread.IsAlive)),
				AccessTools.Method(typeof(UpdateCurrentAsynchronousEventPatch), nameof(IsAliveAndNotPuzzling)));

		public static bool IsAliveAndNotPuzzling(Thread thread)
		{
			bool showingMods = Find.UIRoot != null && Current.Game != null && !LongEventHandler.currentEvent.UseStandardWindow && LongEventHandler.currentEvent.showExtraUIInfo;
			if (showingMods && CheckboxKeepLoadingOpen.keepAlive)
				return true;

			return thread.IsAlive;
		}
	}
}
