﻿using Loodsman;
using LoodsmanCommon.Entities;
using LoodsmanCommon.Entities.Meta;
using LoodsmanCommon.Entities.Meta.Collections;
using PDMObjects;
using System.Data;
using System.Linq;

namespace LoodsmanCommon
{
    public interface ILoodsmanObject
    {
        ILoodsmanObject Parent { get; set; }
        int Id { get; set; }
        LType Type { get; set; }
        string Product { get; set; }
        string Version { get; set; }
        LState State { get; set; }
        bool IsDocument { get; }
        PDMAccessLevels AccessLevel { get; set; }
        PDMLockLevels LockLevel { get; set; }
        NamedEntityCollection<LObjectAttribute> Attributes { get; }
    }

    public class LoodsmanObject : ILoodsmanObject
    {
        private NamedEntityCollection<LObjectAttribute> _attributes;
        private LState _state;
        private readonly ILoodsmanProxy _proxy;

        public ILoodsmanObject Parent { get; set; }
        public int Id { get; set; }
        public LType Type { get; set; }
        public string Product { get; set; }
        public string Version { get; set; }

        public LState State
        {
            get => _state;
            set
            {
                if (_state == value)
                    return;

                _proxy.UpdateState(Id, value);
                _state = value;
            }
        }

        public bool IsDocument => Type.IsDocument;
        public PDMAccessLevels AccessLevel { get; set; }
        public PDMLockLevels LockLevel { get; set; }
        public NamedEntityCollection<LObjectAttribute> Attributes => _attributes ??= new NamedEntityCollection<LObjectAttribute>(() => _proxy.GetAttributes(this), 10);

        public LoodsmanObject(ILoodsmanProxy proxy, LType type, LState state)
        {
            _proxy = proxy;
            Type = type;
            Version = Type.IsVersioned ? Constants.DEFAULT_NEW_VERSION : Constants.DEFAULT_NEW_NO_VERSION;
            _state = state;
        }

        private LoodsmanObject(ILoodsmanProxy proxy, string typeName, string stateName) :
            this(proxy, proxy.Meta.Types[typeName], proxy.Meta.States[stateName])
        { }

        public LoodsmanObject(DataRow dataRow, ILoodsmanProxy proxy) : this(proxy, dataRow["_TYPE"] as string, dataRow["_STATE"] as string)
        {
            Id = (int)dataRow["_ID_VERSION"];
            Product = dataRow["_PRODUCT"] as string;
            Version = dataRow["_VERSION"] as string;
            //IsDocument = (short)dataRow["_DOCUMENT"] == 1;
            AccessLevel = (PDMAccessLevels)dataRow["_ACCESSLEVEL"];
            LockLevel = (PDMLockLevels)dataRow.GetValueOrDefault<int>("_LOCKED");
        }

        public LoodsmanObject(IPluginCall pc, ILoodsmanProxy proxy) : this(proxy, pc.stType, pc.Selected.StateName)
        {
            Id = pc.IdVersion;
            Product = pc.stProduct;
            Version = pc.stVersion;
            AccessLevel = pc.Selected.AccessLevel;
            LockLevel = pc.Selected.LockLevel;
            //IsDocument = pc.Selected.IsDocument;
            Parent = pc.ParentObject is IPDMObject ? new LoodsmanObject(pc.ParentObject, proxy) : null;
        }

        public LoodsmanObject(IPDMObject obj, ILoodsmanProxy proxy) : this(proxy, obj.TypeName, obj.StateName)
        {
            Id = obj.ID;
            Product = obj.Name;
            Version = obj.Version;
            AccessLevel = obj.AccessLevel;
            LockLevel = obj.LockLevel;
            //IsDocument = obj.IsDocument;
            Parent = obj.Parent is IPDMLink link ? new LoodsmanObject(link.ParentObject, proxy) : null;
        }
    }
}
