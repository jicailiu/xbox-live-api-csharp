// Copyright (c) Microsoft Corporation
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Microsoft.Xbox.Services.Stats.Manager
{
    public class StatValue
    {
        public string Name { get; set; }
        public object Value { get; set; }
        public StatValueType Type { get; set; }

        internal StatValue(string name, object value, StatValueType type)
        {
            this.Name = name;
            this.Value = value;
            this.Type = type;
        }

        public int AsInteger()
        {
            return (int)(double)this.Value;
        }

        public string AsString()
        {
            return (string)this.Value;
        }

        public double AsNumber()
        {
            return (double)this.Value;
        }

        internal void SetStat(object value, StatValueType type)
        {
            this.Value = value;
            this.Type = type;
        }
    }
}