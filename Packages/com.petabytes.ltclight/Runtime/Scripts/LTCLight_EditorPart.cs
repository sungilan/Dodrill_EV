#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace petabytes.LTCLight
{

    public partial class LTCLight
    {
        void OnDrawGizmos()
        {
            Gizmos.DrawIcon(transform.position, "Packages/com.petabytes.ltclight/Editor/Resources/icon.png", true);           
        }
    }
}

#endif