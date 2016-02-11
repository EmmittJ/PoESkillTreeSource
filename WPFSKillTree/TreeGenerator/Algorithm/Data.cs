﻿using System;

namespace POESKillTree.TreeGenerator.Algorithm
{
    public interface IData
    {
        GraphEdgeSet EdgeSet { get; }

        DistanceLookup DistanceLookup { get; }

        IDistanceLookup SMatrix { get; }

        GraphNode StartNode { get; set; }
    }

    public class Data : IData
    {
        public GraphEdgeSet EdgeSet { get; set; }
        public DistanceLookup DistanceLookup { get; private set; }
        public IDistanceLookup SMatrix { get; set; }

        private GraphNode _startNode;

        public GraphNode StartNode
        {
            get { return _startNode; }
            set
            {
                _startNode = value;
                if (StartNodeChanged != null)
                    StartNodeChanged(this, _startNode);
            }
        }

        public Data(GraphEdgeSet edgeSet, DistanceLookup distanceLookup, GraphNode startNode)
        {
            EdgeSet = edgeSet;
            DistanceLookup = distanceLookup;
            _startNode = startNode;
        }

        public event EventHandler<GraphNode> StartNodeChanged;
    }
}