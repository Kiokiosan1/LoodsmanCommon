﻿using System.Data;
using System.Linq;
using System.Collections.Generic;
using Ascon.Plm.Loodsman.PluginSDK;
using LoodsmanCommon.Extensions;

namespace LoodsmanCommon.Entities.Meta
{
    public class LType : EntityIcon
    {
        private readonly INetPluginCall _iNetPC;
        private readonly IEnumerable<LAttribute> _lAttributes;
        private IReadOnlyCollection<LTypeAttribute> _attributes;

        /// <summary>
        /// Ключевой атрибут типа.
        /// </summary>
        public LAttribute KeyAttribute { get; }

        /// <summary>
        /// Является ли документом.
        /// </summary>
        public bool IsDocument { get; }

        /// <summary>
        /// Является ли версионным.
        /// </summary>
        public bool IsVersioned { get; }

        /// <summary>
        /// Состояние по умолчанию.
        /// </summary>
        public LState DefaultState { get; }

        /// <summary>
        /// Может ли быть проектом.
        /// </summary>
        public bool CanBeProject { get; }
        
        /// <summary>
        /// Может ли текущий пользователь создавать объекты данного типа.
        /// </summary>
        public bool CanCreate { get; }
        
        /// <summary>
        /// Список возможных атрибутов типа, включая служебные.
        /// </summary>
        public IReadOnlyCollection<LTypeAttribute> Attributes => _attributes ??= _iNetPC.Native_GetInfoAboutType(Name, GetInfoAboutTypeMode.Mode12).GetRows()
                                                .Select(x => new LTypeAttribute(_lAttributes.First(a => a.Id == (int)x["_ID"]), (short)x["_OBLIGATORY"] == 1))
                                                .ToReadOnlyList();

        internal LType(INetPluginCall iNetPC, DataRow dataRow, IEnumerable<LAttribute> lAttributes, IEnumerable<LState> states, string nameField = "_TYPENAME") : base(dataRow, nameField)
        {
            _iNetPC = iNetPC;
            _lAttributes = lAttributes;
            KeyAttribute = _lAttributes.FirstOrDefault(a => a.Name == dataRow["_ATTRNAME"] as string);
            IsDocument = (int)dataRow["_DOCUMENT"] == 1;
            IsVersioned = (int)dataRow["_NOVERSIONS"] == 0;
            DefaultState = states.FirstOrDefault(a => a.Name == dataRow["_DEFAULTSTATE"] as string);
            CanBeProject = (int)dataRow["_CANBEPROJECT"] == 1;
            CanCreate = (int)dataRow["_CANCREATE"] == 1;
        }
    }
}