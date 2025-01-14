﻿using Ascon.Plm.Loodsman.PluginSDK;
using Loodsman;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace LoodsmanCommon
{
  internal class LoodsmanProxy : ILoodsmanProxy
  {
    private readonly ILoodsmanApplication _application;
    private readonly ILoodsmanMeta _meta;
    private string _checkOutName;
    private ILObject _selectedObject;
    private ILObject[] _selectedObjects = new ILObject[] { };

    public LoodsmanProxy(INetPluginCall iNetPC, ILoodsmanMeta loodsmanMeta)
    {
      INetPC = iNetPC;
      _application = (ILoodsmanApplication)INetPC.PluginCall;
      _meta = loodsmanMeta;
    }

    internal INetPluginCall INetPC { get; }

    public ILoodsmanMeta Meta => _meta;

    public ILObject SelectedObject => GetSelectedObject();

    public IEnumerable<ILObject> SelectedObjects => GetSelectedObjects();

    public string CheckOutName => GetCheckOutName();

    private string GetCheckOutName()
    {
      var pluginCall = _application.GetPluginCall();
      return _checkOutName = pluginCall.CheckOut != 0 ? pluginCall.CheckOut.ToString() : string.Empty; ;
    }

    private ILObject GetSelectedObject()
    {
      var pluginCall = _application.GetPluginCall();
      if (pluginCall.IdVersion == 0)
        return null;

      return _selectedObject?.Id == pluginCall.IdVersion ? _selectedObject : _selectedObject = new LObject(pluginCall, this);
    }

    private IEnumerable<ILObject> GetSelectedObjects()
    {
      if (SelectedObject is null)
        return Enumerable.Empty<ILObject>();

      var ids = INetPC.Native_CGetTreeSelectedIDs().Split(new[] { Constants.ID_SEPARATOR }, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();
      if (ids.Length < 2)
        return Enumerable.Repeat(SelectedObject, 1);

      if (_selectedObjects.Select(x => x.Id).OrderBy(x => x).SequenceEqual(ids.OrderBy(x => x)))
        return _selectedObjects;
      
      var selectedObjects = GetPropObjects(ids).ToArray();
      var selectedObject = SelectedObject;
      if (selectedObject != null)
      {
        var index = Array.FindIndex(selectedObjects, x => x.Id == selectedObject.Id);
        if (index >= 0)
          selectedObjects[index] = selectedObject;
      }

      return _selectedObjects = selectedObjects;
    }

    #region NewObject

    public ILObject NewObject(string typeName, string product, string stateName = null, bool isProject = false)
    {
      if (string.IsNullOrEmpty(typeName))
        throw new ArgumentException($"{nameof(typeName)} - тип не может быть пустым", nameof(typeName));

      var type = _meta.Types[typeName];
      var state = string.IsNullOrEmpty(stateName) ? type.DefaultState : _meta.States[stateName];
      var id = INetPC.Native_NewObject(type.Name, state.Name, product, isProject);
      if (type.IsBO)
      {
        var location = product;
        product = INetPC.Native_GetProductFromBO(type.Name, product);
        return new LObject(this, id, product, type, state) { BOLocation = location };
      }

      return new LObject(this, id, product, type, state);
    }

    private string StateIfNullGetDefault(string typeName, string stateName = null)
    {
      if (string.IsNullOrEmpty(typeName))
        throw new ArgumentException($"{nameof(typeName)} - тип не может быть пустым", nameof(typeName));

      if (string.IsNullOrEmpty(stateName))
        stateName = _meta.Types[typeName].DefaultState.Name;

      return stateName;
    }
    #endregion

    #region Link - Insert/New/Update/Remove
    public int InsertObject(ILObject parent, ILObject child, string linkType, string stateName = null, bool reuse = false)
    {
      CheckLoodsmanObjectsForError(parent, child);
      CheckInsertedObject(parent);
      CheckInsertedObject(child);
      return InsertObject(parent.Type.Name, parent.Name, parent.Version, linkType, child.Type.Name, child.Name, child.Version, stateName, reuse);
    }

    private void CheckInsertedObject(ILObject loodsmanObject)
    {
      if (loodsmanObject.Id <= 0 && string.IsNullOrEmpty(loodsmanObject.Version))
        loodsmanObject.Version = Constants.DEFAULT_INSERT_NEW_VERSION;
    }

    public int InsertObject(string parentTypeName, string parentProduct, string parentVersion, string linkType, string childTypeName, string childProduct, string childVersion = Constants.DEFAULT_INSERT_NEW_VERSION, string stateName = null, bool reuse = false)
    {
      CheckKeyAttributesForErrors(parentTypeName, parentProduct, childTypeName, childProduct);
      if (string.IsNullOrEmpty(stateName))
        stateName = StateIfNullGetDefault(parentVersion == Constants.DEFAULT_INSERT_NEW_VERSION ? parentTypeName : childTypeName);

      return INetPC.Native_InsertObject(parentTypeName, parentProduct, parentVersion, linkType, childTypeName, childProduct, childVersion, stateName, reuse);
    }

    public LLink NewLink(ILObject parent, ILObject child, string linkType, double minQuantity = 0, double maxQuantity = 0, string unitId = null)
    {
      CheckLoodsmanObjectsForError(parent, child);
      if (parent.Id <= 0 && child.Id <= 0)
      {
        if (string.IsNullOrEmpty(parent.Name) && string.IsNullOrEmpty(child.Name))
          throw new InvalidOperationException("Не заданы ключевые атрибуты объектов для формирования связи");
      }
      else
      {
        if (parent.Id <= 0)
          NewObject(parent.Type.Name, parent.Name);

        if (child.Id <= 0)
          NewObject(child.Type.Name, child.Name);
      }
      
      var id = NewLink(parent.Id, parent.Type.Name, parent.Name, parent.Version, child.Id, child.Type.Name, child.Name, child.Version, linkType, minQuantity, maxQuantity, unitId);
      
      return new LLink(this, id, linkType, parent, child, minQuantity, maxQuantity, null, unitId);
    }


    public int NewLink(string parentTypeName, string parentProduct, string parentVersion, string childTypeName, string childProduct, string childVersion, string linkType, double minQuantity = 0, double maxQuantity = 0, string unitId = null)
    {
      CheckKeyAttributesForErrors(parentTypeName, parentProduct, childTypeName, childProduct);
      return NewLink(0, parentTypeName, parentProduct, parentVersion, 0, childTypeName, childProduct, childVersion, linkType, minQuantity, maxQuantity, unitId);
    }

    public int NewLink(int parentId, int childId, string linkType, double minQuantity = 0, double maxQuantity = 0, string unitId = null)
    {
      if (parentId <= 0)
        throw new ArgumentException($"{nameof(parentId)} - отсутствует или неверно задан идентификатор объекта", nameof(parentId));

      if (childId <= 0)
        throw new ArgumentException($"{nameof(childId)} - отсутствует или неверно задан идентификатор объекта", nameof(childId));

      return INetPC.Native_NewLink(parentId, string.Empty, string.Empty, string.Empty, childId, string.Empty, string.Empty, string.Empty, minQuantity, maxQuantity, unitId, linkType);
    }

    public void UpLink(int idLink, double minQuantity = 0, double maxQuantity = 0, string unitId = null)
    {
      INetPC.Native_UpLink(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, idLink, minQuantity, maxQuantity, unitId, false, string.Empty);
    }

    public void DeleteLink(int idLink)
    {
      INetPC.Native_UpLink(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, idLink, 0, 0, string.Empty, true, string.Empty);
    }

    private int NewLink(int parentId, string parentTypeName, string parentProduct, string parentVersion, int childId, string childTypeName, string childProduct, string childVersion, string linkType, double minQuantity, double maxQuantity, string unitId)
    {
      if (string.IsNullOrEmpty(linkType))
        throw new ArgumentException($"{nameof(linkType)} не может быть пустым, не указан тип связи", nameof(linkType));

      return INetPC.Native_NewLink(parentId, parentTypeName, parentProduct, parentVersion, childId, childTypeName, childProduct, childVersion, minQuantity, maxQuantity, unitId, linkType);
    }

    private static void CheckKeyAttributesForErrors(string parentTypeName, string parentProduct, string childTypeName, string childProduct)
    {
      if (string.IsNullOrEmpty(parentTypeName))
        throw new ArgumentException($"{nameof(parentTypeName)} не может быть пустым или иметь значение null", nameof(parentTypeName));

      if (string.IsNullOrEmpty(parentProduct))
        throw new ArgumentException($"{nameof(parentProduct)} не может быть пустым или иметь значение null", nameof(parentProduct));

      if (string.IsNullOrEmpty(childTypeName))
        throw new ArgumentException($"{nameof(childTypeName)} не может быть пустым или иметь значение null", nameof(childTypeName));

      if (string.IsNullOrEmpty(childProduct))
        throw new ArgumentException($"{nameof(childProduct)} не может быть пустым или иметь значение null", nameof(childProduct));
    }

    private static void CheckLoodsmanObjectsForError(ILObject parent, ILObject child)
    {
      if (parent is null)
        throw new ArgumentNullException($"{nameof(parent)} не задан родитель для создания связи");

      if (child is null)
        throw new ArgumentNullException($"{nameof(child)} не задан потомок для создания связи");
    }

    public IEnumerable<ILObject> GetLinkedFast(int objectId, string linkType, bool inverse = false)
    {
      return INetPC.Native_GetLinkedFast(objectId, linkType, inverse).Select(x => new LObject(x, this));
    }

    public IEnumerable<ILLink> GetLinkedFast(ILObject lObject, string linkType, bool inverse = false)
    {
      var items = INetPC.Native_GetLinkedFast(lObject.Id, linkType, inverse);
      return !inverse ? items.Select(x => new LLink(this, x.ID_LINK(), linkType, lObject, new LObject(x, this), x.MAX_QUANTITY(), x.MIN_QUANTITY(), x.ID_UNIT(), x.ID_MEASURE())) :
                        items.Select(x => new LLink(this, x.ID_LINK(), linkType, new LObject(x, this), lObject, x.MAX_QUANTITY(), x.MIN_QUANTITY(), x.ID_UNIT(), x.ID_MEASURE()));
    }  
    #endregion

    public string GetBOLocation(int objectId)
    {
      return INetPC.Native_GetInfoAboutVersion(objectId, GetInfoAboutVersionMode.Mode16).FirstOrDefault()?.NAME();
    }

    public CreationInfo GetCreationInfo(int objectId)
    {
      var dtCreationInfo = INetPC.Native_GetInfoAboutVersion(objectId, GetInfoAboutVersionMode.Mode13).Rows[0];
      var creator = Meta.Users.TryGetValue(dtCreationInfo.NAME(), out var lUser) ? lUser : null;
      var created = dtCreationInfo.DATEOFCREATE();
      return new CreationInfo { Creator = creator, Created = created };
    }

    public IEnumerable<ILAttribute> GetAttributes(ILObject loodsmanObject)
    {
      var attributesInfo = INetPC.Native_GetInfoAboutVersion(loodsmanObject.Id, GetInfoAboutVersionMode.Mode3).Select(x => x);
      foreach (var item in loodsmanObject.Type.Attributes)
      {
        yield return GetAttribute(loodsmanObject, attributesInfo, item);
      }
    }

    public IEnumerable<ILAttribute> GetLinkAttributes(ILLink link)
    {
      var attributesInfo = INetPC.Native_GetLinkAttributes2(link.Id, GetAttributeMode.All).Select(x => x);
      foreach (var item in link.LinkInfo.Attributes)
      {
        yield return GetAttribute(link, attributesInfo, item);
      }
    }

    private ILAttribute GetAttribute(ILAttributeOwner owner, IEnumerable<DataRow> attributesValues, ILAttributeInfo item)
    {
      var attribute = attributesValues.FirstOrDefault(x => x.NAME() == item.Name);
      var measureId = string.Empty;
      var unitId = string.Empty;
      var value = string.Empty;
      if (attribute != null)
      {
        value = attribute.VALUE();
        if (item.IsMeasured)
        {
          measureId = attribute.ID_MEASURE();
          unitId = attribute.ID_UNIT();
        }
      }

      return new LAttribute(this, owner, item, value, measureId, unitId);
    }

    public double ConverseValue(double value, LMeasureUnit sourceMeasureUnit, LMeasureUnit destMeasureUnit)
    {
      if (sourceMeasureUnit is null || destMeasureUnit is null || sourceMeasureUnit == destMeasureUnit)
        return value;

      if (sourceMeasureUnit.ParentMeasure != destMeasureUnit.ParentMeasure)
        throw new ArgumentException($"Невозможно преобразование единиц измерения из \"{sourceMeasureUnit.ParentMeasure.Name}\" в \"{destMeasureUnit.ParentMeasure.Name}\"");

      return INetPC.Native_ConverseValue(value, sourceMeasureUnit.Guid, destMeasureUnit.Guid);
    }

    public void UpdateState(int objectId, LStateInfo state)
    {
      if (state is null)
        throw new Exception("Состоянием не может быть пустым");

      INetPC.Native_UpdateStateOnObject(objectId, state.Name);
    }

    public void UpLinkAttrValueById(int objectId, string attributeName, object attributeValue, LMeasureUnit measureUnit = null)
    {
      INetPC.Native_UpLinkAttrValue(objectId, attributeName, attributeValue, measureUnit?.Guid, IsNullOrDefault(attributeValue));
    }

    public void UpAttrValueById(int objectId, string attributeName, object attributeValue, LMeasureUnit measureUnit = null)
    {
      INetPC.Native_UpAttrValueById(objectId, attributeName, attributeValue, measureUnit?.Guid, IsNullOrDefault(attributeValue));
    }

    public static bool IsNullOrDefault<T>(T value)
    {
      return value == null || (value is string strValue && strValue == string.Empty);
    }

    public IEnumerable<LFile> GetFiles(ILObject lObject)
    {
      return !lObject.IsDocument ? Enumerable.Empty<LFile>() : INetPC.Native_GetInfoAboutVersion(lObject.Id, GetInfoAboutVersionMode.Mode7).Select(x => new LFile(this, lObject, x));
    }

    public string GetFile(ILObject lObject, string fileName, string folderPath)
    {
      if (!lObject.IsDocument)
        return string.Empty;

      return INetPC.Native_GetFile(lObject.Id, fileName, folderPath);
    }

    public string RegistrationOfFile(int documentId, string fileName, string folderPath, string filePath)
    {
      try
      {
        filePath = CopyIfNeddedOnWorkDir(filePath, fileName, folderPath);
        INetPC.Native_RegistrationOfFile(documentId, fileName, folderPath);
        return filePath;
      }
      catch// (Exception ex)
      {
        return string.Empty;
        //var test = ex;
        //logger?
      }
    }
    public string RegistrationOfFile(string typeName, string product, string version, string fileName, string folderPath, string filePath)
    {
      try
      {
        filePath = CopyIfNeddedOnWorkDir(filePath, fileName, folderPath);
        INetPC.Native_RegistrationOfFile(typeName, product, version, fileName, folderPath);
        return filePath;
      }
      catch
      {
        return string.Empty;
        //logger?
      }
    }

    public void SaveSecondaryView(int docId, string filePath, bool removeAfterSave = true)
    {
      filePath = CopyIfNeddedOnWorkDir(filePath);
      INetPC.Native_SaveSecondaryView(docId, filePath);

      if (removeAfterSave)
        try { File.Delete(filePath); } catch { }
    }

    private string CopyIfNeddedOnWorkDir(string filePath, string fileName = "", string folderPath = "")
    {
      var path = Path.Combine(_meta.CurrentUser.FileDir, folderPath);
      if (!filePath.Contains(path))
      {
        Directory.CreateDirectory(path);
        if (string.IsNullOrEmpty(fileName))
          fileName = Path.GetFileName(filePath);

        var newPath = Path.Combine(path, fileName);
        File.Copy(filePath, newPath, true);
        filePath = newPath;
      }

      return filePath;
    }

    public bool CheckUniqueName(string typeName, string product)
    {
      return INetPC.Native_CheckUniqueName(typeName, product).Rows.Count != 0;
    }

    public bool CheckFileNameEx(string fileName, string filePath)
    {
      return INetPC.Native_CheckFileNameEx(fileName, filePath).Rows.Count != 0;
    }

    public DataTable GetReport(string reportName, IEnumerable<int> objectsIds = null, string reportParams = null)
    {
      return INetPC.Native_GetReport(reportName, objectsIds, reportParams);
    }

    public IEnumerable<ILObject> GetPropObjects(IEnumerable<int> objectsIds)
    {
      return INetPC.Native_GetPropObjects(objectsIds).Select(x => new LObject(x, this));
    }

    public ILObject PreviewBoObject(string typeName, string uniqueId)
    {
      var xmlString = INetPC.Native_PreviewBoObject(typeName, uniqueId);
      var xDocument = XDocument.Parse(xmlString);
      var elements = xDocument.Descendants("PreviewBoObjectResult").Elements();
      var type = _meta.Types[typeName];
      var stateName = elements.FirstOrDefault(x => x.Name == "State")?.Value;
      var state = string.IsNullOrEmpty(stateName) ? type.DefaultState : _meta.States[stateName];
      var idVersion = int.TryParse(elements.FirstOrDefault(x => x.Name == "VersionId")?.Value, out var id) ? id : 0;
      var product = elements.FirstOrDefault(x => x.Name == "Product").Value;
      return new LObject(this, idVersion, product, type, state);
    }

    public IEnumerable<int> GetLockedObjectsIds()
    {
      return INetPC.Native_GetLockedObjects().Select(x => (int)x[0]);
    }


    #region KillVersion
    public void KillVersion(int id)
    {
      INetPC.Native_KillVersion(id);
    }

    public void KillVersion(IEnumerable<int> objectsIds)
    {
      INetPC.Native_KillVersions(objectsIds);
    }

    public void KillVersion(string typeName, string product, string version)
    {
      INetPC.Native_KillVersion(typeName, product, version);
    }
    #endregion

    #region CheckOut
    public string CheckOut(string typeName, string product, string version, CheckOutMode mode = CheckOutMode.Default)
    {
      return _checkOutName = INetPC.Native_CheckOut(typeName, product, version, mode);
    }

    public string SelectedObjectCheckOut(CheckOutMode mode = CheckOutMode.Default)
    {
      var pluginCall = _application.GetPluginCall();
      var wasCheckout = pluginCall.CheckOut != 0;
      _checkOutName = wasCheckout ? pluginCall.CheckOut.ToString() : CheckOut(pluginCall.stType, pluginCall.stProduct, pluginCall.stVersion, mode);
      if (!wasCheckout)
        ConnectToCheckOut(_checkOutName, pluginCall.DBName);

      return _checkOutName;
    }

    public void ConnectToCheckOut(string checkOutName = null, string dBName = null)
    {
      var localCheckOutName = checkOutName ?? _checkOutName;
      if (string.IsNullOrEmpty(localCheckOutName))
        return;

      INetPC.Native_ConnectToCheckOut(localCheckOutName, dBName ?? _application.GetPluginCall().DBName);
    }

    public void DisconnectToCheckOut(string checkOutName = null, string dBName = null)
    {
      var localCheckOutName = checkOutName ?? _checkOutName;
      if (string.IsNullOrEmpty(localCheckOutName))
        return;

      INetPC.Native_DisconnectCheckOut(localCheckOutName, dBName ?? _application.GetPluginCall().DBName);
    }

    public void AddToCheckOut(int objectId, bool isRoot = false)
    {
      INetPC.Native_AddToCheckOut(objectId, isRoot);
    }

    public void CheckIn(string checkOutName = null, string dBName = null)
    {
      INetPC.Native_CheckIn(checkOutName ?? _checkOutName, dBName ?? _application.GetPluginCall().DBName);
      _checkOutName = string.Empty;
    }

    public void SaveChanges(string checkOutName = null, string dBName = null)
    {
      INetPC.Native_SaveChanges(checkOutName ?? _checkOutName, dBName ?? _application.GetPluginCall().DBName);
    }

    public void CancelCheckOut(string checkOutName = null, string dBName = null)
    {
      var localCheckOutName = checkOutName ?? _checkOutName;
      if (string.IsNullOrEmpty(localCheckOutName))
        return;

      INetPC.Native_CancelCheckOut(localCheckOutName, dBName ?? _application.GetPluginCall().DBName);
      _checkOutName = string.Empty;
    }
    #endregion
  }
}
