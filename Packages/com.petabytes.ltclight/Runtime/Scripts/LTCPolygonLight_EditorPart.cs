#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;

namespace petabytes.LTCLight
{
    public partial class LTCPolygonLight
    {
        public PolygonPreset Preset
        {
            get => polygonPreset_;
            set
            {
                polygonPreset_ = value;
                SetPolygonPreset(value);
            }
        }
        
        [SerializeField] private PolygonPreset polygonPreset_;
        private bool setPointsMannually_ = true;
    }
}
#endif