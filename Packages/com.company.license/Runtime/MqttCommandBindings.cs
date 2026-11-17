using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace Company.License
{
    [Serializable]
    public class MqttCommandEvent : UnityEvent<string> { }

    [Serializable]
    public class MqttCommandBinding
    {
        [Tooltip("Paneldeki komut tipi (ornek: mri.slide). Backend'de backend/src/mqtt/commands.ts icinde tanimli olmali.")]
        public string commandType;

        [Tooltip("Komut gelince cagrilir. Parametre: komutun payload'i (ham JSON, ornek {\"to\":\"inside\"}).")]
        public MqttCommandEvent onCommand = new MqttCommandEvent();
    }

    // Kod yazmadan komut -> fonksiyon esleme: sahneye ekleyip Inspector'dan komut tipini
    // bir metoda baglayin. Kod tarafinda ayni isi MqttCommandManager.RegisterHandler yapar.
    // Bilesen aktifken handler'lar kayitlidir; devre disi kalinca kaldirilir.
    public class MqttCommandBindings : MonoBehaviour
    {
        public List<MqttCommandBinding> bindings = new List<MqttCommandBinding>();

        private void OnEnable()
        {
            foreach (var binding in bindings)
            {
                if (binding == null || string.IsNullOrEmpty(binding.commandType)) continue;
                var captured = binding;
                MqttCommandManager.RegisterHandler(captured.commandType, cmd => captured.onCommand.Invoke(cmd.PayloadJson));
            }
        }

        private void OnDisable()
        {
            foreach (var binding in bindings)
            {
                if (binding != null) MqttCommandManager.UnregisterHandler(binding.commandType);
            }
        }
    }
}
