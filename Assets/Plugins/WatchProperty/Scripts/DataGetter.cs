using MTST_RCS;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AmagiSakuya.ObservableVariables
{
    public class DataGetter : DataSelector
    {
        [SerializeField] protected float frameInterval = 5.0f;

        protected virtual void Update()
        {
            if (Time.frameCount % frameInterval != 0) return;
            Type propType =  GetSelectedPropertyGenericType();
            OnDataGetUpdate(propType);
        }

        protected virtual void OnDataGetUpdate(Type propType)
        {

        }
    }
}