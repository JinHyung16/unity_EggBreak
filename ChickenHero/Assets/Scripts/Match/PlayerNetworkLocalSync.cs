using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerNetworkLocalSync : MonoBehaviour
{
    private Player player;


    private void Start()
    {
        player = GetComponentInChildren<Player>();
    }

    private void LateUpdate()
    {
        if (!player.IsThrow)
        {
            return;
        }

        GameManager.GetInstance.SendMatchState(OpCodes.Point,
            MatchDataJson.Point(player.HitPoint));
    }

}