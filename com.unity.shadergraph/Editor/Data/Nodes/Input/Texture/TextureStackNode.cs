using System.Linq;
using UnityEngine;
using UnityEditor.Graphing;
using System.Collections.Generic;
using System;
using System.Globalization;
using UnityEditor.ShaderGraph.Drawing.Controls;

namespace UnityEditor.ShaderGraph
{
    //[Title("Input", "Texture", "Sample Stack")]
    class SampleTextureStackNodeBase : AbstractMaterialNode, IGeneratesBodyCode, IMayRequireMeshUV
    {
        public const int UVInputId = 0;

        [NonSerialized]
        public int[] OutputSlotIds = new int[4];

        [NonSerialized]
        public int[] TextureInputIds = new int[4];

        [NonSerialized]
        public int FeedbackSlotId;

        static string[] OutputSlotNames = { "Out", "Out2", "Out3", "Out4" };
        static string[] TextureInputNames = { "Texture", "Texture2", "Texture3", "Texture4" };
        const string UVInputNAme = "UV";
        const string FeedbackSlotName = "Feedback";

        int numSlots;
        int[] liveIds;

        public override bool hasPreview { get { return false; } }

        [SerializeField]
        protected TextureType[] m_TextureTypes = { TextureType.Default, TextureType.Default, TextureType.Default, TextureType.Default };

        // We have one normal/object space field for all layers for now, probably a nice compromise
        // between lots of settings and user flexibility?
        [SerializeField]
        private NormalMapSpace m_NormalMapSpace = NormalMapSpace.Tangent;

        [EnumControl("Space")]
        public NormalMapSpace normalMapSpace
        {
            get { return m_NormalMapSpace; }
            set
            {
                if (m_NormalMapSpace == value)
                    return;

                m_NormalMapSpace = value;
                Dirty(ModificationScope.Graph);
            }
        }

        public SampleTextureStackNodeBase(int numSlots)
        {
            if (numSlots > 4)
            {
                throw new System.Exception("Maximum 4 slots supported");
            }
            this.numSlots = numSlots;
            name = "Sample Texture Stack " + numSlots;

            UpdateNodeAfterDeserialization();
        }

        public override void UpdateNodeAfterDeserialization()
        {
            // Allocate IDs
            List<int> usedSlots = new List<int>();
            usedSlots.Add(UVInputId);

            for (int i = 0; i < numSlots; i++)
            {
                OutputSlotIds[i] = UVInputId + 1 + i;
                TextureInputIds[i] = UVInputId + 1 + numSlots + i;

                usedSlots.Add(OutputSlotIds[i]);
                usedSlots.Add(TextureInputIds[i]);
            }

            FeedbackSlotId = UVInputId + 1 + numSlots * 2;
            usedSlots.Add(FeedbackSlotId);

            liveIds = usedSlots.ToArray();

            // Create slots
            AddSlot(new UVMaterialSlot(UVInputId, UVInputNAme, UVInputNAme, UVChannel.UV0));

            for (int i = 0; i < numSlots; i++)
            {
                AddSlot(new Vector4MaterialSlot(OutputSlotIds[i], OutputSlotNames[i], OutputSlotNames[i], SlotType.Output, Vector4.zero, ShaderStageCapability.Fragment));
            }

            for (int i = 0; i < numSlots; i++)
            {
                AddSlot(new Texture2DInputMaterialSlot(TextureInputIds[i], TextureInputNames[i], TextureInputNames[i]));
            }

            var slot = new Vector4MaterialSlot(FeedbackSlotId, FeedbackSlotName, FeedbackSlotName, SlotType.Output, Vector4.zero, ShaderStageCapability.Fragment);
            slot.hidden = true;
            AddSlot(slot);

            RemoveSlotsNameNotMatching(liveIds);
        }

        public override void ValidateNode()
        {
            for (int i = 0; i < numSlots; i++)
            {
                var textureSlot = FindInputSlot<Texture2DInputMaterialSlot>(TextureInputIds[i]);
                textureSlot.defaultType = (m_TextureTypes[i] == TextureType.Normal ? TextureShaderProperty.DefaultType.Bump : TextureShaderProperty.DefaultType.White);
            }
            base.ValidateNode();
        }

        public override PreviewMode previewMode
        {
            get { return PreviewMode.Preview3D; }
        }

        // Node generations
        public virtual void GenerateNodeCode(ShaderStringBuilder sb, GraphContext graphContext, GenerationMode generationMode)
        {
            // Not all outputs may be connected (well one is or we wouln't get called) so we are carefull to
            // only generate code for connected outputs
            string stackName = GetVariableNameForSlot(OutputSlotIds[0]) + "_texturestack";

            bool anyConnected = false;
            for (int i = 0; i < numSlots; i++)
            {
                if (IsSlotConnected(OutputSlotIds[i]))
                {
                    anyConnected = true;
                    break;
                }
            }
            bool feedbackConnected = IsSlotConnected(FeedbackSlotId); ;
            anyConnected |= feedbackConnected;

            if (anyConnected)
            {
                string result = string.Format("StackInfo {0}_info = PrepareStack({1}, {0});"
                        , stackName
                        , GetSlotValue(UVInputId, generationMode));
                sb.AppendLine(result);
            }

            for (int i = 0; i < numSlots; i++)
            {
                if (IsSlotConnected(OutputSlotIds[i]))
                {
                    var id = GetSlotValue(TextureInputIds[i], generationMode);
                    string resultLayer = string.Format("$precision4 {1} = SampleStack({0}_info, {2});"
                            , stackName
                            , GetVariableNameForSlot(OutputSlotIds[i])
                            , id);
                    sb.AppendLine(resultLayer);
                }
            }

            for (int i = 0; i < numSlots; i++)
            {
                if (IsSlotConnected(OutputSlotIds[i]))
                {

                    if (m_TextureTypes[i] == TextureType.Normal)
                    {
                        if (normalMapSpace == NormalMapSpace.Tangent)
                        {
                            sb.AppendLine(string.Format("{0}.rgb = UnpackNormalmapRGorAG({0});", GetVariableNameForSlot(OutputSlotIds[i])));
                        }
                        else
                        {
                            sb.AppendLine(string.Format("{0}.rgb = UnpackNormalRGB({0});", GetVariableNameForSlot(OutputSlotIds[i])));
                        }
                    }
                }
            }

            if (feedbackConnected)
            {
                //TODO: Investigate if the feedback pass can use halfs
                string feedBackCode = string.Format("float4 {0} = GetResolveOutput({1}_info);",
                        GetVariableNameForSlot(FeedbackSlotId),
                        stackName);
                sb.AppendLine(feedBackCode);
            }
        }

        public override void CollectShaderProperties(PropertyCollector properties, GenerationMode generationMode)
        {
            base.CollectShaderProperties(properties, generationMode);

            // Get names of connected textures
            List<string> slotNames = new List<string>();
            for (int i = 0; i < numSlots; i++)
            {
                var id = GetSlotValue(TextureInputIds[i], generationMode);
                slotNames.Add(id);
            }

            string stackName = GetVariableNameForSlot(OutputSlotIds[0]) + "_texturestack";

            // Add attributes to any connected textures
            int found = 0;
            foreach (var prop in properties.properties.OfType<TextureShaderProperty>())
            {
                foreach (var inputTex in slotNames)
                {
                    if (string.Compare(inputTex, prop.referenceName) == 0)
                    {
                        prop.textureStack = stackName;
                        found++;
                    }
                }
            }

            if (found != slotNames.Count)
            {
                Debug.LogWarning("Could not find some texture properties for stack " + stackName);
            }

            properties.AddShaderProperty(new StackShaderProperty()
            {
                overrideReferenceName = stackName,
                generatePropertyBlock = true,
                modifiable = false,
                slotNames = slotNames
            });
        }

        public bool RequiresMeshUV(UVChannel channel, ShaderStageCapability stageCapability)
        {
            s_TempSlots.Clear();
            GetInputSlots(s_TempSlots);
            foreach (var slot in s_TempSlots)
            {
                if (slot.RequiresMeshUV(channel))
                    return true;
            }
            return false;
        }
    }

    [Title("Input", "Texture", "Sample Texture Stack")]
    class SampleTextureStackNode : SampleTextureStackNodeBase
    {
        public SampleTextureStackNode() : base(1)
        { }

        [EnumControl("Type")]
        public TextureType textureType
        {
            get { return m_TextureTypes[0]; }
            set
            {
                if (m_TextureTypes[0] == value)
                    return;

                m_TextureTypes[0] = value;
                Dirty(ModificationScope.Graph);

                ValidateNode();
            }
        }
    }

    [Title("Input", "Texture", "Sample Texture Stack 2")]
    class SampleTextureStackNode2 : SampleTextureStackNodeBase
    {
        public SampleTextureStackNode2() : base(2)
        { }

        [EnumControl("Type 1")]
        public TextureType textureType
        {
            get { return m_TextureTypes[0]; }
            set
            {
                if (m_TextureTypes[0] == value)
                    return;

                m_TextureTypes[0] = value;
                Dirty(ModificationScope.Graph);

                ValidateNode();
            }
        }

        [EnumControl("Type 2")]
        public TextureType textureType2
        {
            get { return m_TextureTypes[1]; }
            set
            {
                if (m_TextureTypes[1] == value)
                    return;

                m_TextureTypes[1] = value;
                Dirty(ModificationScope.Graph);

                ValidateNode();
            }
        }
    }

    [Title("Input", "Texture", "Sample Texture Stack 3")]
    class SampleTextureStackNode3 : SampleTextureStackNodeBase
    {
        public SampleTextureStackNode3() : base(3)
        { }

        [EnumControl("Type 1")]
        public TextureType textureType
        {
            get { return m_TextureTypes[0]; }
            set
            {
                if (m_TextureTypes[0] == value)
                    return;

                m_TextureTypes[0] = value;
                Dirty(ModificationScope.Graph);

                ValidateNode();
            }
        }

        [EnumControl("Type 2")]
        public TextureType textureType2
        {
            get { return m_TextureTypes[1]; }
            set
            {
                if (m_TextureTypes[1] == value)
                    return;

                m_TextureTypes[1] = value;
                Dirty(ModificationScope.Graph);

                ValidateNode();
            }
        }

        [EnumControl("Type 3")]
        public TextureType textureType3
        {
            get { return m_TextureTypes[2]; }
            set
            {
                if (m_TextureTypes[2] == value)
                    return;

                m_TextureTypes[2] = value;
                Dirty(ModificationScope.Graph);

                ValidateNode();
            }
        }
    }

    [Title("Input", "Texture", "Sample Texture Stack 4")]
    class SampleTextureStackNode4 : SampleTextureStackNodeBase
    {
        public SampleTextureStackNode4() : base(4)
        { }

        [EnumControl("Type 1")]
        public TextureType textureType
        {
            get { return m_TextureTypes[0]; }
            set
            {
                if (m_TextureTypes[0] == value)
                    return;

                m_TextureTypes[0] = value;
                Dirty(ModificationScope.Graph);

                ValidateNode();
            }
        }

        [EnumControl("Type 2")]
        public TextureType textureType2
        {
            get { return m_TextureTypes[1]; }
            set
            {
                if (m_TextureTypes[1] == value)
                    return;

                m_TextureTypes[1] = value;
                Dirty(ModificationScope.Graph);

                ValidateNode();
            }
        }

        [EnumControl("Type 3")]
        public TextureType textureType3
        {
            get { return m_TextureTypes[2]; }
            set
            {
                if (m_TextureTypes[2] == value)
                    return;

                m_TextureTypes[2] = value;
                Dirty(ModificationScope.Graph);

                ValidateNode();
            }
        }
    }

    class TextureStackAggregateFeedbackNode : AbstractMaterialNode, IGeneratesBodyCode, IMayRequireRequirePixelCoordinate
    {
        public const int AggregateOutputId = 0;
        const string AggregateOutputName = "FeedbackAggregateOut";

        public const int AggregateInputFirstId = 1;

        public override bool hasPreview { get { return false; } }
        
        public TextureStackAggregateFeedbackNode()
        {
            name = "Feedback Aggregate";
            UpdateNodeAfterDeserialization();
        }


        public override void UpdateNodeAfterDeserialization()
        {
            AddSlot(new Vector4MaterialSlot(AggregateOutputId, AggregateOutputName, AggregateOutputName, SlotType.Output, Vector4.zero, ShaderStageCapability.Fragment));
            RemoveSlotsNameNotMatching(new int[] { AggregateOutputId });
        }

        public override PreviewMode previewMode
        {
            get { return PreviewMode.Preview3D; }
        }

        // Node generations
        public virtual void GenerateNodeCode(ShaderStringBuilder sb, GraphContext graphContext, GenerationMode generationMode)
        {
            var slots = this.GetInputSlots<ISlot>();
            int numSlots = slots.Count();
            if (numSlots == 0)
            {
                return;
            }

            if (numSlots == 1)
            {
                string feedBackCode = $"float4 {GetVariableNameForSlot(AggregateOutputId)} = {GetSlotValue(AggregateInputFirstId, generationMode)};";
                sb.AppendLine(feedBackCode);
            }
            else if (numSlots > 1)
            {
                string arrayName = $"{GetVariableNameForSlot(AggregateOutputId)}_array";
                sb.AppendLine($"float4 {arrayName}[{numSlots}];");

                int arrayIndex = 0;
                foreach (var slot in slots)
                {
                    string code = $"{arrayName}[{arrayIndex}] = {GetSlotValue(AggregateInputFirstId + arrayIndex, generationMode)};";
                    sb.AppendLine(code);
                    arrayIndex++;
                }

                string feedBackCode = $"float4 {GetVariableNameForSlot(AggregateOutputId)} = {arrayName}[ (IN.{ShaderGeneratorNames.PixelCoordinate}.x  + _FrameCount )% (uint){numSlots}];";

                sb.AppendLine(feedBackCode);
            }
        }

        public bool RequiresPixelCoordinate(ShaderStageCapability stageCapability)
        {
            var slots = this.GetInputSlots<ISlot>();
            int numSlots = slots.Count();
            return numSlots > 1;
        }

    }

    static class VirtualTexturingFeedback
    {
        public const int OutputSlotID = 22021982;
        
        // Automatically add a  streaming feedback node and correctly connect it to stack samples are connected to it and it is connected to the master node output
        public static IMasterNode AutoInject(IMasterNode iMasterNode) 
        {
            var masterNode = iMasterNode as AbstractMaterialNode;
            var stackNodes = GraphUtil.FindDownStreamNodesOfType<SampleTextureStackNodeBase>(masterNode);

            // Early out if there are no VT nodes in the graph
            if ( stackNodes.Count <= 0 )
            {
                return null;
            }
            
            // Duplicate the Graph so we can modify it
            var workingMasterNode = masterNode.owner.ScratchCopy().GetNodeFromGuid(masterNode.guid);// as MasterNode<T>;

            // inject VTFeedback output slot
            var vtFeedbackSlot = new Vector4MaterialSlot(OutputSlotID, "VTFeedback", "VTFeedback", SlotType.Input, Vector4.one, ShaderStageCapability.Fragment);
            vtFeedbackSlot.hidden = true;
            workingMasterNode.AddSlot(vtFeedbackSlot);

            // Inject Aggregate node
            var feedbackNode = new TextureStackAggregateFeedbackNode();
            workingMasterNode.owner.AddNode(feedbackNode);

            // Add inputs to feedback node
            int i = 0;
            foreach (var node in stackNodes)
            {
                // Find feedback output slot on the vt node
                var stackFeedbackOutputSlot = (node.FindOutputSlot<ISlot>(node.FeedbackSlotId)) as Vector4MaterialSlot;
                if (stackFeedbackOutputSlot == null)
                {
                    Debug.LogWarning("Could not find the VT feedback output slot on the stack node.");
                    return null;
                }

                // Create a new slot on the aggregate that is similar to the uv input slot
                string name = "FeedIn_" + i;
                var newSlot = new Vector4MaterialSlot(TextureStackAggregateFeedbackNode.AggregateInputFirstId + i, name, name, SlotType.Input, Vector4.zero, ShaderStageCapability.Fragment);
                newSlot.owner = feedbackNode;
                feedbackNode.AddSlot(newSlot);

                feedbackNode.owner.Connect(stackFeedbackOutputSlot.slotReference, newSlot.slotReference);
                i++;
            }

            // Add input to master node
            var feedbackInputSlot = workingMasterNode.FindInputSlot<ISlot>(OutputSlotID);
            if ( feedbackInputSlot == null )
            {
                Debug.LogWarning("Could not find the VT feedback input slot on the master node.");
                return null;
            }

            var feedbackOutputSlot = feedbackNode.FindOutputSlot<ISlot>(TextureStackAggregateFeedbackNode.AggregateOutputId);
            if ( feedbackOutputSlot == null )
            {
                Debug.LogWarning("Could not find the VT feedback output slot on the aggregate node.");
                return null;
            }

            workingMasterNode.owner.Connect(feedbackOutputSlot.slotReference, feedbackInputSlot.slotReference);
            workingMasterNode.owner.ClearChanges();

            return workingMasterNode as IMasterNode;
        }   
    }
}
