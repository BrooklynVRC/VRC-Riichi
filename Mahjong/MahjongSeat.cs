using UdonSharp;
using UnityEngine;

/// Seat button at the mahjong table: Sit / stand, or (with botButton) add / remove a bot in that seat.
[UdonBehaviourSyncMode(BehaviourSyncMode.None)]
public class MahjongSeat : UdonSharpBehaviour
{
    public MahjongGame game;
    public int seatIndex;
    public bool botButton;

    public override void Interact()
    {
        if (game == null) return;
        if (botButton) game.ToggleBot(seatIndex);
        else game.ToggleSeat(seatIndex);
    }
}
