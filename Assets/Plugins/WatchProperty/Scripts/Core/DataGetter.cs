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

        /// <summary>
        /// 勾选后，组件在 OnEnable 时会对所选 WatchProperty 调用 ForceNotifyCurrent()，
        /// 强制让订阅方按当前值刷新一次。
        /// </summary>
        [SerializeField] protected bool forceNotifyOnEnable = false;

        protected override void OnEnable()
        {
            base.OnEnable();

            if (!forceNotifyOnEnable) return;

            IWatchProperty prop = GetSelectedWatchProperty() as IWatchProperty;
            if (prop != null)
            {
                prop.ForceNotifyCurrent();
            }
        }

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