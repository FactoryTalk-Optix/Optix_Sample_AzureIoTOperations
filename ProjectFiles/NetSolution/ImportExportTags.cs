#region Using directives
using System;
using System.Text;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.HMIProject;
using FTOptix.NetLogic;
using FTOptix.Core;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using FTOptix.CommunicationDriver;
using FTOptix.S7TCP;
using FTOptix.S7TiaProfinet;
using FTOptix.Alarm;
using System.Text.RegularExpressions;
using FTOptix.RAEtherNetIP;
#endregion

public class ImportExportTags : BaseNetLogic
{
    private string _tagsCsvUri;
    private IUANode _startingNode;
    private const string _csvSeparator = ";";
    private const string _CSV_FILENAME = "tags.csv";
    private const string _arrayLengthSeparator = ",";
    private int _tagsCreated = 0;
    private int _tagsUpdated = 0;
    private int _tagStructuresCreated = 0;
    private static readonly List<string> _customTagsPropertiesNames = new List<string> { "Type", "BrowseName", "BrowsePath", "NodeDataType", "ArrayLength" };
    private List<string> _tagsPropertiesNames;

    [ExportMethod]
    public void ExportToCsv()
    {
        RetrieveParameters();
        WriteTagsToCsv(_startingNode);
    }

    [ExportMethod]
    public void ImportOrUpdateFromCsv()
    {
        RetrieveParameters();
        try
        {
            using (StreamReader reader = new StreamReader(_tagsCsvUri))
            {
                string line = reader.ReadLine();
                string[] header = line.Split(_csvSeparator);
                if (line == null) return;
                while ((line = reader.ReadLine()) != null)
                {
                    string[] values = line.Split(_csvSeparator);
                    CreateOrUpdateTagFromCsvLine(values, header);
                }
            }
            Log.Info(LogicObject.BrowseName, "Tags updated: " + _tagsUpdated + " Tags created: " + _tagsCreated + " TagStructure created: " + _tagStructuresCreated);
        }
        catch (System.Exception ex)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, ex.Message);
        }
    }

    private void CreateOrUpdateTagFromCsvLine(string[] values, string[] header)
    {
        try
        {
            var tagTypeString = values[GetElementIndex(header, _customTagsPropertiesNames[0])];
            if (tagTypeString == typeof(FTOptix.CommunicationDriver.TagStructure).FullName)
            {
                var arrayDim = GetArrayLengthString(values, header);
                if (arrayDim != string.Empty)
                {
                    var tagStructureArray = InformationModel.MakeVariable<FTOptix.CommunicationDriver.TagStructure>(
                        GetBrowseName(values, header),
                        OpcUa.DataTypes.Structure,
                        new uint[] { uint.Parse(arrayDim) });
                    GenerateTagStructure(values, header, tagStructureArray);
                }
                else
                {
                    var tagStructure = InformationModel.MakeVariable<TagStructure>(GetBrowseName(values, header), UAManagedCore.OpcUa.DataTypes.Structure);
                    GenerateTagStructure(values, header, tagStructure);
                }
            }
            else
            {
                GenerateTag(values, header, tagTypeString);
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, ex.Message);
        }
    }

    private void GenerateTagStructure(string[] values, string[] header, UAVariable tStructure)
    {
        var tagBrowsePath = GetBrowsePath(values, header);
        var owner = GetOwnerNode(_startingNode, tagBrowsePath, true, false);
        var alreadyExistingNode = NodeAlreadyExists(owner, tStructure) != null;

        if (!alreadyExistingNode)
        {
            owner.Add(tStructure);
            _tagStructuresCreated++;
        };
    }

    private void GenerateTag(string[] values, string[] header, string tagTypeString)
    {
        try
        {
            FTOptix.CommunicationDriver.Tag tag;
            var tagBrowseName = GetBrowseName(values, header);
            var tagBrowsePath = GetBrowsePath(values, header);
            var tagDataTypeString = GetDataTypeString(values, header);
            var tagArrayLengthString = GetArrayLengthString(values, header);
            if (tagTypeString == typeof(FTOptix.S7TCP.Tag).FullName) tag = InformationModel.Make<FTOptix.S7TCP.Tag>(tagBrowseName);
            else if (tagTypeString == typeof(FTOptix.S7TiaProfinet.Tag).FullName) tag = InformationModel.Make<FTOptix.S7TiaProfinet.Tag>(tagBrowseName);
            else if (tagTypeString == typeof(FTOptix.CODESYS.Tag).FullName) tag = InformationModel.Make<FTOptix.CODESYS.Tag>(tagBrowseName);
            else if (tagTypeString == typeof(FTOptix.Modbus.Tag).FullName) tag = InformationModel.Make<FTOptix.Modbus.Tag>(tagBrowseName);
            else if (tagTypeString == typeof(FTOptix.RAEtherNetIP.Tag).FullName) tag = InformationModel.Make<FTOptix.RAEtherNetIP.Tag>(tagBrowseName);
            else throw new NotImplementedException();

            tag.DataType = GetOpcUaDataType(tagDataTypeString);
            if (tagArrayLengthString != string.Empty) SetTagArrayDimensions(tag, tagArrayLengthString);

            PropertyInfo[] tagProperties = GetTypePropertieInfos(tag.GetType());
            foreach (var p in tagProperties)
            {
                if (!p.CanWrite) continue;
                var gropertyCsvIndex = GetElementIndex(header, p.Name);
                if (gropertyCsvIndex == -1) continue;
                var v = values[gropertyCsvIndex];
                if (string.IsNullOrEmpty(v)) continue;
                if (p.PropertyType.IsEnum)
                {
                    if (p.PropertyType.Name == "ValueRank") continue;
                    var enumValue = Enum.Parse(p.PropertyType, v);
                    SetPropertyValue(tag, p, enumValue);
                }
                else
                {
                    switch (Type.GetTypeCode(p.PropertyType))
                    {
                        case TypeCode.Int16:
                            SetPropertyValue(tag, p.Name, short.Parse(v));
                            break;
                        case TypeCode.Int32:
                            SetPropertyValue(tag, p.Name, int.Parse(v));
                            break;
                        case TypeCode.UInt16:
                            SetPropertyValue(tag, p.Name, ushort.Parse(v));
                            break;
                        case TypeCode.UInt32:
                            SetPropertyValue(tag, p.Name, uint.Parse(v));
                            break;
                        case TypeCode.Double:
                            SetPropertyValue(tag, p.Name, double.Parse(v));
                            break;
                        case TypeCode.Byte:
                            SetPropertyValue(tag, p.Name, byte.Parse(v));
                            break;
                        case TypeCode.Boolean:
                            SetPropertyValue(tag, p.Name, bool.Parse(v));
                            break;
                        case TypeCode.String:
                            SetPropertyValue(tag, p.Name, v.ToString());
                            break;
                        default:
                            break;
                    }
                }
            }

            var owner = GetOwnerNode(_startingNode, tagBrowsePath, true, LogicObject.GetVariable("MakeFolderInsteadStruct").Value);
            var alreadyExistingTag = NodeAlreadyExists(owner, tag);

            if (alreadyExistingTag != null)
            {
                TagsUpdate(alreadyExistingTag, tag);
                _tagsUpdated++;
            }
            else
            {
                owner.Add(tag);
                _tagsCreated++;
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, ex.Message);
        }
    }

    private static void SetTagArrayDimensions(FTOptix.CommunicationDriver.Tag tag, string tagArrayLengthString)
    {
        var isDataMatrix = tagArrayLengthString.Contains(_arrayLengthSeparator);
        if (isDataMatrix)
        {
            var indexes = tagArrayLengthString.Split(_arrayLengthSeparator);
            var index0 = uint.Parse(indexes[0]);
            var index1 = uint.Parse(indexes[1]);
            tag.ArrayDimensions = new uint[] { index0, index1 };
        }
        else
        {
            var index = uint.Parse(tagArrayLengthString);
            tag.ArrayDimensions = new uint[] { index };
        }
    }

    private static void TagsUpdate(IUANode destinationTag, FTOptix.CommunicationDriver.Tag sourceTag)
    {
        try
        {
            var destinationValueType = ((UAManagedCore.UAVariable)destinationTag).Value.Value.GetType();
            var sourceValueType = sourceTag.Value.Value.GetType();
            if (destinationValueType != sourceValueType)
            {
                Log.Error("Tag " + destinationTag.BrowseName + " cannot be updated because its type is " + destinationValueType + " and the imported data says " + sourceValueType);
                return;
            }
            var tagProperties = GetPropertiesNamesFromTagType(sourceTag.GetType());
            foreach (var p in tagProperties)
            {
                object v = GetPropertyValue(sourceTag, p);
                if (v == null) continue;
                SetPropertyValue(destinationTag, p, v);
            }
        }
        catch (System.Exception ex)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, ex.StackTrace + " " + ex.Message);
        }
    }

    private static IUANode NodeAlreadyExists(IUANode tagOwner, IUANode tag) => tagOwner?.Children.FirstOrDefault(t => t.BrowseName == tag.BrowseName);

    private IUANode GetOwnerNode(IUANode startingNode, string relativePath, bool generateOwner, bool generateFolder)
    {
        // set the owner with starting node
        IUANode tagOwner = startingNode;
        // if starting node is null, return null
        if (tagOwner == null) return null;
        // split the path string to retrive an array with all owners of the node
        string[] pathElements = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        // Jump the first owner (the ancestral is linked to netlogic) and check if all owners exists
        for (int i=1; i<pathElements.Length-1; i++)
        {
            // If the owner not exist and the flag generate is true, create a new TagStructure and add to current tagOwner
            if (tagOwner?.Get(pathElements[i]) == null && generateOwner) 
            {
                // in case of the flag generate folder is true, make folder instead TagStructure
                if (generateFolder)
                    tagOwner.Add(InformationModel.Make<Folder>(pathElements[i]));
                else
                {
                    tagOwner.Add(InformationModel.MakeVariable<TagStructure>(pathElements[i], OpcUa.DataTypes.Structure));
                    _tagStructuresCreated++;
                }            
            }
            // Retrive the sub owner by get function of actual owner
            tagOwner = tagOwner?.Get(pathElements[i]);
        }
        // return the near owner of the node
        return tagOwner;
    }

    private static int GetElementIndex(string[] array, string key) => Array.IndexOf(array, key);

    private void RetrieveParameters()
    {
        _tagsCsvUri = ResourceUri.FromProjectRelativePath(_CSV_FILENAME).Uri;
        _startingNode = InformationModel.Get(LogicObject.GetVariable("StartingNodeToFetch").Value);
    }

    private void WriteTagsToCsv(IUANode startingNode)
    {
        try
        {
            File.Create(_tagsCsvUri).Close();
            var tag = GetOneOfTheTags(startingNode);
            var csvHeader = GenerateCsvHeader(tag);
            var tagPropertiesNames = GetTagPropertiesNames(tag);
            var tagsAndStructures = GetTagsAndStructures(startingNode);

            var tags = tagsAndStructures.Item1;
            var tagsStructures = tagsAndStructures.Item2.Where(t => t.ArrayDimensions.Length == 0);
            var tagsStructureArrays = tagsAndStructures.Item2.Where(t => t.ArrayDimensions.Length != 0);

            System.Text.Encoding encoding = System.Text.Encoding.Unicode;

            using (StreamWriter sWriter = new(_tagsCsvUri, false, encoding))
            {
                sWriter.WriteLine(csvHeader);
                foreach (var t in tagsStructureArrays) WriteTagOnCsv<FTOptix.CommunicationDriver.TagStructure>(t, tagPropertiesNames, sWriter);
                foreach (var t in tagsStructures) WriteTagOnCsv<FTOptix.CommunicationDriver.TagStructure>(t, tagPropertiesNames, sWriter);
                foreach (var t in tags) WriteTagOnCsv<FTOptix.CommunicationDriver.Tag>(t, tagPropertiesNames, sWriter);
            }

            Log.Info("Tags exported: " + tags.Count);
            Log.Info("Tag structures exported: " + tagsStructures.Count());
            Log.Info("Tag structure arrays exported: " + tagsStructureArrays.Count());
        }
        catch (System.Exception ex)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, ex.Message);
        }
    }

    private void WriteTagOnCsv<T>(T t, List<string> tagPropertiesNames, StreamWriter sWriter)
    {
        var tRow = GetPropertyInfoFromTag<T>(t, tagPropertiesNames);
        sWriter.WriteLine(String.Join(_csvSeparator, tRow));
    }

    private IUANode GetOneOfTheTags(IUANode startingNode)
    {
        if (startingNode is FTOptix.CommunicationDriver.Tag) return startingNode;
        foreach (var c in startingNode.Children)
        {
            if (c.GetType().Name == typeof(FTOptix.CommunicationDriver.Tag).Name) return c;
            return GetOneOfTheTags(c);
        }
        return null;
    }

    private static HashSet<string> GetPropertiesNamesFromTagType(Type type)
    {
        var propertiesNames = new HashSet<string>();
        foreach (PropertyInfo p in GetTypePropertieInfos(type))
        {
            if (p.PropertyType.Name != typeof(IUAVariable).Name) propertiesNames.Add(p.Name);
        }
        return propertiesNames;
    }

    private static PropertyInfo[] GetTypePropertieInfos(Type type) => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);


    private List<string> GetTagPropertiesNames(IUANode t)
    {
        var tagProperties = GetPropertiesNamesFromTagType(t.GetType());
        return ComposeCustomAndNativeTagPropertiesNames(tagProperties);
    }

    private List<string> ComposeCustomAndNativeTagPropertiesNames(HashSet<string> tagProperties)
    {
        _tagsPropertiesNames = new List<string>();
        _tagsPropertiesNames.AddRange(_customTagsPropertiesNames);
        _tagsPropertiesNames.AddRange(tagProperties);
        return _tagsPropertiesNames;
    }

    private string GenerateCsvHeader(IUANode t) => string.Join(_csvSeparator, GetTagPropertiesNames(t));
    private List<string> GetPropertyInfoFromTag<T>(T t, List<string> tagProperties)
    {
        try
        {
            Dictionary<string, string> tagPropertiesDict = new();
            tagProperties = tagProperties.Distinct().ToList();

            switch (t)
            {
                case FTOptix.CommunicationDriver.Tag:
                    var imATag = (t as FTOptix.CommunicationDriver.Tag);
                    tagPropertiesDict.Add(tagProperties[0], imATag.GetType().FullName);
                    tagPropertiesDict.Add(tagProperties[1], imATag.BrowseName);
                    tagPropertiesDict.Add(tagProperties[2], GetBrowsePath(_startingNode, imATag, "/"));
                    tagPropertiesDict.Add(tagProperties[3], InformationModel.Get(imATag.DataType).BrowseName);
                    var tagArrayDim = imATag.ArrayDimensions.Length == 0 ?
                                            string.Empty
                                            : imATag.ArrayDimensions.Length == 1 ?
                                            imATag.ArrayDimensions[0].ToString()
                                            : imATag.ArrayDimensions[0].ToString() + _arrayLengthSeparator + imATag.ArrayDimensions[1].ToString();
                    tagPropertiesDict.Add(tagProperties[4], tagArrayDim);
                    for (int i = 5; i < tagProperties.Count; i++)
                    {
                        var property = tagProperties[i];
                        var propertyVal = GetPropertyValue(imATag, property);
                        tagPropertiesDict.Add(property, propertyVal == null ? string.Empty : propertyVal.ToString());
                    }
                    break;
                case FTOptix.CommunicationDriver.TagStructure:
                    var imATagStructure = (t as FTOptix.CommunicationDriver.TagStructure);
                    tagPropertiesDict.Add(tagProperties[0], imATagStructure.GetType().FullName);
                    tagPropertiesDict.Add(tagProperties[1], imATagStructure.BrowseName);
                    tagPropertiesDict.Add(tagProperties[2], GetBrowsePath(_startingNode, imATagStructure, "/"));
                    tagPropertiesDict.Add(tagProperties[3], string.Empty);

                    if (imATagStructure.ArrayDimensions.Length == 0)
                    {
                        tagPropertiesDict.Add(tagProperties[4], string.Empty);
                    }
                    else
                    {
                        var tagStructureArrayDim = imATagStructure.ArrayDimensions[0] == 0 ? string.Empty : imATagStructure.ArrayDimensions[0].ToString();
                        tagPropertiesDict.Add(tagProperties[4], tagStructureArrayDim);
                    }
                    break;
                default:
                    break;
            }

            return tagPropertiesDict.Select(kv => kv.Value).ToList();
        }
        catch (System.Exception ex)
        {
            Log.Error(MethodBase.GetCurrentMethod().Name, ex.Message);
            return new List<string>();
        }
    }

    private static (List<FTOptix.CommunicationDriver.Tag>, List<TagStructure>) GetTagsAndStructures(IUANode startingNode)
    {
        var tuple = (new List<FTOptix.CommunicationDriver.Tag>(), new List<FTOptix.CommunicationDriver.TagStructure>());

        foreach (var t in startingNode.Children)
        {
            switch (t)
            {
                case FTOptix.CommunicationDriver.Tag _:
                    tuple.Item1.Add((FTOptix.CommunicationDriver.Tag)t);
                    break;
                case FTOptix.CommunicationDriver.TagStructure _:
                    tuple.Item2.Add((FTOptix.CommunicationDriver.TagStructure)t);
                    tuple = MergeTuples(tuple, (GetTagsAndStructures(t)));
                    break;
                default:
                    tuple = MergeTuples(tuple, (GetTagsAndStructures(t)));
                    break;
            }
        }
        return tuple;
    }

    private static (List<FTOptix.CommunicationDriver.Tag>, List<TagStructure>) MergeTuples((List<FTOptix.CommunicationDriver.Tag>, List<TagStructure>) tuple1, (List<FTOptix.CommunicationDriver.Tag>, List<TagStructure>) tuple2) => (tuple1.Item1.Concat(tuple2.Item1).ToList(), tuple1.Item2.Concat(tuple2.Item2).ToList());

    private static string GetBrowsePath(IUANode startingNode, IUANode uANode, string sepatator)
    {
        var browsePath = string.Empty;
        var isStartingNode = uANode.NodeId == startingNode.NodeId;

        if (isStartingNode) return startingNode.BrowseName + browsePath;

        return GetBrowsePath(startingNode, uANode.Owner, sepatator) + sepatator + uANode.BrowseName;
    }

    private static string GetBrowseName(string[] values, string[] header) => values[GetElementIndex(header, _customTagsPropertiesNames[1])];
    private static string GetBrowsePath(string[] values, string[] header) => values[GetElementIndex(header, _customTagsPropertiesNames[2])];
    private static string GetDataTypeString(string[] values, string[] header) => values[GetElementIndex(header, _customTagsPropertiesNames[3])];
    private static string GetArrayLengthString(string[] values, string[] header) => values[GetElementIndex(header, _customTagsPropertiesNames[4])];
    private static void SetPropertyValue(FTOptix.CommunicationDriver.Tag tag, PropertyInfo propertyInfo, object val) => SetPropertyValue(tag, propertyInfo.Name, val);
    private static void SetPropertyValue(FTOptix.CommunicationDriver.Tag tag, string propertyName, object val) => tag.GetType().GetProperty(propertyName).SetValue(tag, val);
    private static void SetPropertyValue(IUANode tag, string propertyName, object val) => tag.GetType().GetProperty(propertyName).SetValue(tag, val);
    private static PropertyInfo GetProperty<T>(T tag, string propertyName) => tag.GetType().GetProperty(propertyName);
    private static object GetPropertyValue<T>(T tag, string propertyName) => GetProperty(tag, propertyName).GetValue(tag);

    private static NodeId GetOpcUaDataType(string tagDataTypeString)
    {
        object objRet=null;
        // Create the regex for finding type incasesensitive
        Regex regexPat = new Regex($@"(?i)\b{tagDataTypeString}\b");
        // With the regex check inside the Fields of OpcUA.DataType for get the NodeID of UA Type
        NodeId NodeIdType = (NodeId)typeof(OpcUa.DataTypes).GetFields().Where(x => regexPat.Match(x.Name).Success && x.FieldType == typeof(NodeId)).First().GetValue(objRet);
        // in case of success, return the NodeId, otherwise try with DataTypesHelper
        if (NodeIdType != null) return NodeIdType;
        var tagNetType = GetNetTypeFromOPCUAType(tagDataTypeString);

        switch (Type.GetTypeCode(tagNetType))
        {
            case TypeCode.SByte:
                return OpcUa.DataTypes.SByte;
            case TypeCode.Int16:
                return OpcUa.DataTypes.Int16;
            case TypeCode.Int32:
                return OpcUa.DataTypes.Int32;
            case TypeCode.Int64:
                return OpcUa.DataTypes.Int64;
            case TypeCode.Byte:
                return OpcUa.DataTypes.Byte;
            case TypeCode.UInt16:
                return OpcUa.DataTypes.UInt16;
            case TypeCode.UInt32:
                return OpcUa.DataTypes.UInt32;
            case TypeCode.UInt64:
                return OpcUa.DataTypes.UInt64;
            case TypeCode.Boolean:
                return OpcUa.DataTypes.Boolean;
            case TypeCode.Double:
                return OpcUa.DataTypes.Double;
            case TypeCode.Single:
                return OpcUa.DataTypes.Float;
            case TypeCode.String:
                return OpcUa.DataTypes.String;
            case TypeCode.DateTime:
                return OpcUa.DataTypes.DateTime;
            default:
                return OpcUa.DataTypes.BaseDataType;
        }
    }

    private static Type GetNetTypeFromOPCUAType(string dataTypeString)
    {
        var netType = DataTypesHelper.GetNetTypeByDataTypeName(dataTypeString);
        return netType ?? throw new Exception($"Type corresponding to {dataTypeString} was not found in OPCUA namespace");
    }
}
