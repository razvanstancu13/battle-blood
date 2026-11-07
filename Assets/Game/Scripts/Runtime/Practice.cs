using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace BattleBlood
{
    /// <summary>What a practice bout is: your soldier's type and the opponent's.</summary>
    public class PracticeSetup
    {
        public UnitType Mine = UnitType.Infantry, Theirs = UnitType.Infantry;
        public int Opponent;       // 0 fights, 1 stands still, 2 stands and blocks
        public static readonly string[] OpponentNames = { "fights back", "stands still", "stands and blocks" };
    }

    /// <summary>
    /// The practice bout's own keys and help line: one soldier against one, on a clear patch of the same generated land.
    /// Blows land, bleed and stagger but take no health, so nobody dies and the moves can be watched as long as wanted.
    /// </summary>
    public class Practice : MonoBehaviour
    {
        Battle b;
        Text help;
        bool slow;

        public void Init(Battle battle)
        {
            b = battle;
            var canvas = UiKit.MakeCanvas("Practice", 12);
            help = UiKit.Label(canvas.transform, "help", new Vector2(0, 1), new Vector2(24, -24), new Vector2(760, 200), "", 22, TextAnchor.UpperLeft, new Color(0.92f, 0.87f, 0.76f));
            help.rectTransform.pivot = new Vector2(0, 1);
            Refresh();
        }

        void Refresh()
        {
            var s = Battle.PracticeMode;
            help.text = $"PRACTICE  ·  you: {s.Mine}  ·  opponent: {s.Theirs}, {PracticeSetup.OpponentNames[s.Opponent]}  ·  no damage\n" +
                        "1 2 3  your soldier: infantry, archer, cavalry     4 5 6  the opponent's\n" +
                        "P  opponent fights / stands / blocks     R  start again     T  slow motion " + (slow ? "(on)" : "(off)") + "     Esc  menu";
        }

        void Update()
        {
            if (b == null || !b.Ready || PlayerControl.Paused) return;
            var kb = Keyboard.current;
            if (kb == null) return;
            var s = Battle.PracticeMode;
            bool respawn = false;
            for (int i = 0; i < 3; i++)
            {
                if (kb[Key.Digit1 + i].wasPressedThisFrame) { s.Mine = (UnitType)i; respawn = true; }
                if (kb[Key.Digit4 + i].wasPressedThisFrame) { s.Theirs = (UnitType)i; respawn = true; }
            }
            if (kb.rKey.wasPressedThisFrame) respawn = true;
            if (kb.pKey.wasPressedThisFrame) { s.Opponent = (s.Opponent + 1) % 3; ApplyOpponent(); Refresh(); }
            if (kb.tKey.wasPressedThisFrame) { slow = !slow; Battle.SpeedMul = slow ? 0.3f : 1f; Refresh(); }
            if (respawn) { b.SpawnPractice(); ApplyOpponent(); Refresh(); }
        }

        void ApplyOpponent()
        {
            foreach (var u in b.Army(Unit.Red)) u.Passive = Battle.PracticeMode.Opponent;
        }

        void OnDestroy() { Battle.SpeedMul = 1f; }
    }
}
