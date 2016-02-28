﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Equus.Horse;
using Equus.Calabrese;
using Equus.Andalusian;
using Equus.QuarterHorse;
using Equus.Nokota;
using Equus.Shire;

namespace Equus.Thoroughbred.Seabiscut
{

    public sealed class RowCluster
    {

        private RowClusterRule _rule;
        private RecordSet _means;
        private RowClusterInitializer _initializer;
        private DataSet _data;
        private Predicate _where;
        private int _count;
        private FNodeSet _fields;
        private int _zero_fail_itterations = 0;
        private int _zero_fail_counts = 0;
        private int _max_itterations = 25;
        private int _actual_itterations = -1;
        private double _exit_condition = 0.005;
        private FNode _weight;

        public RowCluster(DataSet Data, Predicate Where, FNodeSet Fields, FNode Weight, int Count)
        {
            this._data = Data;
            this._where = Where;
            this._fields = Fields;
            this._count = Count;
            this._rule = new RowClusterRuleEuclid();
            this._initializer = new RowClusterInitializerSpectrum();
            this._means = this._initializer.Initialize(Data, Where, Fields, Count);
            this._weight = Weight;
        }

        public RowCluster(DataSet Data, Predicate Where, FNodeSet Fields, int Count)
            : this(Data, Where, Fields, FNodeFactory.Value(1D), Count)
        {
        }

        public int MaxItterations
        {
            get { return this._max_itterations; }
            set { this._max_itterations = value; }
        }

        public double ExitCondition
        {
            get { return this._exit_condition; }
            set { this._exit_condition = value; }
        }

        public int ActualItterations
        {
            get
            {
                return this._actual_itterations;
            }
        }

        public RecordSet Means
        {
            get { return this._means; }
        }

        public RowClusterRule DistanceMeasure
        {
            get { return this._rule; }
            set { this._rule = value; }
        }

        // Methods //
        public void Render()
        {

            // Loop over each itteration //
            for (int i = 0; i < 25; i++)
            {

                // Do one itteration //
                bool should_exit = this.ItterateOnce();
                
                // Check if we should exit //
                if (should_exit)
                {
                    this._actual_itterations = i;
                    break;
                }

            }

        }

        private bool ItterateOnce()
        {

            // Create the cluster mapping FNode; this node does the nearest neighbor test //
            FNodeSet keys = new FNodeSet();
            FNode n = new FNodeResult(null, new RowClusterCellFunction(this._rule, this._means));
            foreach (FNode t in this._fields.Nodes)
            {
                n.AddChildNode(t.CloneOfMe());
            }
            keys.Add("CLUSTER_ID", n);

            // Create the aggregate variables //
            AggregateSet set = new AggregateSet();
            for (int i = 0; i < this._fields.Count; i++)
            {
                set.Add(new AggregateAverage(this._fields[i].CloneOfMe()), this._fields.Alias(i));
            }

            // Run the aggregate; this is basically a horse aggregate step with the cluster node mapping as the key, and averaging as the value
            RecordSet rs = AggregatePlan.Render(this._data, this._where, keys, set);
            
            // Check for cluster misses; cluster misses occur when no node maps to a cluster correctly //
            if (rs.Count != this._means.Count)
            {
                this.HandleNullCluster(rs);
            }

            // Compare the changes between itterations
            double change = this.CompareChanges(this._means, rs);
            
            // Set the means to the newly calculated means //
            this._means = rs;
            
            // Return a boolean indicating if we failed or not
            return change < this._exit_condition;

        }

        private void HandleNullCluster(RecordSet Means)
        {

            // Increment the fail itterations //
            this._zero_fail_itterations++;

            // Find which nodes are missing //
            List<int> MissingKeys = new List<int>();
            for (int i = 0; i < this._count; i++)
            {
                if (Means.Seek(new Cell(i), 0) == -1)
                {
                    this._zero_fail_counts++;
                    MissingKeys.Add(i);
                }
            }

            // Add back the missing nodes from the current itteration //
            foreach (int x in MissingKeys)
            {
                RecordBuilder rb = new RecordBuilder();
                rb.Add(x);
                rb.Add(Record.Subrecord(this._means[x], 1, this._means.Columns.Count - 1));
                Means.Add(rb.ToRecord());
            }

        }

        private double CompareChanges(RecordSet Current, RecordSet New)
        {

            Key k = Key.Build(1, Current.Columns.Count - 1);
            Current.Sort(k);
            New.Sort(k);

            double Distance = 0;
            for (int i = 0; i < Current.Count; i++)
            {

                for (int j = 0; j < Current.Columns.Count; j++)
                {

                    Distance += Math.Pow(Current[i][j].DOUBLE - New[i][j].DOUBLE, 2);

                }

            }

            return Distance;

        }

        public void Extend(RecordWriter Output, DataSet Data, FNodeSet ClusterVariables, FNodeSet OtherKeepers, Predicate Where)
        {

            // Check that the ClusterVariable count matches the internal node set count //
            if (ClusterVariables.Count != this._fields.Count)
                throw new ArgumentException("The cluster variable count passed does not match the internal cluster variable count");

            // Create the selectors //
            FNodeSet values = OtherKeepers.CloneOfMe();
            FNode n = new FNodeResult(null, new RowClusterCellFunction(this._rule, this._means));
            foreach (FNode t in ClusterVariables.Nodes)
            {
                n.AddChildNode(t.CloneOfMe());
            }
            values.Add("CLUSTER_ID", n);

            // Run a fast select //
            FastReadPlan plan = new FastReadPlan(Data, Where, values, Output);

        }

        public RecordSet Extend(DataSet Data, FNodeSet ClusterVariables, FNodeSet OtherKeepers, Predicate Where)
        {

            // Check that the ClusterVariable count matches the internal node set count //
            if (ClusterVariables.Count != this._fields.Count)
                throw new ArgumentException("The cluster variable count passed does not match the internal cluster variable count");

            // Create the selectors //
            FNodeSet values = OtherKeepers.CloneOfMe();
            FNode n = new FNodeResult(null, new RowClusterCellFunction(this._rule, this._means));
            foreach (FNode t in ClusterVariables.Nodes)
            {
                n.AddChildNode(t.CloneOfMe());
            }
            values.Add("CLUSTER_ID", n);

            // Build a recordset //
            RecordSet rs = new RecordSet(values.Columns);
            RecordWriter w = rs.OpenWriter();

            // Run a fast select //
            FastReadPlan plan = new FastReadPlan(Data, Where, values, w);
            plan.Execute();
            w.Close();

            return rs;

        }

        public Table Extend(string Dir, string Name, DataSet Data, FNodeSet ClusterVariables, FNodeSet OtherKeepers, Predicate Where)
        {

            // Check that the ClusterVariable count matches the internal node set count //
            if (ClusterVariables.Count != this._fields.Count)
                throw new ArgumentException("The cluster variable count passed does not match the internal cluster variable count");

            // Create the selectors //
            FNodeSet values = OtherKeepers.CloneOfMe();
            FNode n = new FNodeResult(null, new RowClusterCellFunction(this._rule, this._means));
            foreach (FNode t in ClusterVariables.Nodes)
            {
                n.AddChildNode(t.CloneOfMe());
            }
            values.Add("CLUSTER_ID", n);

            // Build a recordset //
            Table tablix = new Table(Dir, Name, values.Columns, Data.MaxRecords);
            RecordWriter w = tablix.OpenWriter();

            // Run a fast select //
            FastReadPlan plan = new FastReadPlan(Data, Where, values, w);
            w.Close();

            return tablix;

        }

        public string Statistics()
        {

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Row_Cluster");
            sb.AppendLine("Dimensions: " + this._fields.Count.ToString());
            sb.AppendLine("Clusters: " + this._count.ToString());
            sb.AppendLine("Actual Itterations: " + this._actual_itterations.ToString());
            sb.AppendLine("Zero Match Fails: " + this._zero_fail_counts.ToString());
            sb.AppendLine("Centroids:");
            sb.AppendLine(this._means.Columns.ToNameString());
            foreach (Record r in this._means._Cache)
            {
                sb.AppendLine(r.ToString('\t'));
            }
            return sb.ToString();

        }

    }

    public abstract class RowClusterRule
    {

        public RowClusterRule()
        {
        }

        public abstract double Distance(Cell[] Value, Record Mean);

        public virtual int NearestNeighbor(Cell[] Value, RecordSet Means)
        {

            double current_distance = 0;
            int current_index = 0;
            double min_distance = double.MaxValue;
            int min_index = 0;
            //Console.WriteLine("------------------------------------------------");
            foreach (Record r in Means._Cache)
            {

                Record t = Record.Subrecord(r, 1, r.Count - 1); // the first value of the mean record is always the key

                current_distance = this.Distance(Value, t);
                //Console.WriteLine("Current {0} : Min {1} | Cur {2} : Min {3}", current_distance, min_distance, current_index, min_index);
                if (current_distance < min_distance)
                {
                    min_distance = current_distance;
                    min_index = current_index;
                }
                current_index++;

            }

            return min_index;

        }

    }

    public sealed class RowClusterRuleEuclid : RowClusterRule
    {

        public override double Distance(Cell[] Value, Record Mean)
        {

            if (Value.Length != Mean.Count)
                throw new IndexOutOfRangeException(string.Format("Set passed has a different length ({0}) than the record passed ({1})", Value.Length, Mean.Count));

            double d = 0;
            for (int i = 0; i < Value.Length; i++)
            {
                d += Math.Pow(Value[i].DOUBLE - Mean[i].DOUBLE, 2D);
            }
            return d;

        }

    }

    public sealed class RowClusterRuleChi : RowClusterRule
    {

        public override double Distance(Cell[] Value, Record Mean)
        {

            if (Value.Length != Mean.Count)
                throw new IndexOutOfRangeException(string.Format("Set passed has a different length ({0}) than the record passed ({1})", Value.Length, Mean.Count));

            double d = 0;
            double num = 0;
            double denom = 0;
            for (int i = 0; i < Value.Length; i++)
            {
                denom = Math.Pow(Value[i].DOUBLE, 2D) + Math.Pow(Mean[i].DOUBLE, 2D);
                num = Math.Pow(Value[i].DOUBLE - Mean[i].DOUBLE, 2D);
                d += (denom == 0D ? 0D : num / denom);
            }
            return d;

        }

    }

    public sealed class RowClusterRuleGauss : RowClusterRule
    {

        public override double Distance(Cell[] Value, Record Mean)
        {

            if (Value.Length != Mean.Count)
                throw new IndexOutOfRangeException(string.Format("Set passed has a different length ({0}) than the record passed ({1})", Value.Length, Mean.Count));

            double d = 0;
            for (int i = 0; i < Value.Length; i++)
            {
                d += Math.Exp(-Math.Pow(Value[i].DOUBLE - Mean[i].DOUBLE, 2D));
            }
            return d;

        }

    }

    public static class RowClusterRuleFactory
    {

        public static RowClusterRule Euclid
        {
            get { return new RowClusterRuleEuclid(); }
        }

        public static RowClusterRuleChi Chi
        {
            get { return new RowClusterRuleChi(); }
        }

        public static RowClusterRuleGauss Gauss
        {
            get { return new RowClusterRuleGauss(); }
        }

    }

    public abstract class RowClusterInitializer
    {

        public RowClusterInitializer()
        {
        }

        public abstract RecordSet Initialize(DataSet Data, Predicate Where, FNodeSet Fields, int Clusters);

    }

    public sealed class RowClusterInitializerRandom : RowClusterInitializer
    {

        public RowClusterInitializerRandom(int Seed)
            :base()
        {
        }

        public int Seed
        {
            get;
            set;
        }

        public override RecordSet Initialize(DataSet Data, Predicate Where, FNodeSet Fields,  int Clusters)
        {

            AggregateSet set = new AggregateSet();
            for (int i = 0; i < Fields.Count; i++)
            {
                set.Add(new AggregateAverage(Fields[i].CloneOfMe()), Fields.Alias(i));
            }

            FNode rnd = new FNodeResult(null, new CellRandomInt());
            rnd.AddChildNode(new FNodeValue(rnd, new Cell(this.Seed)));
            rnd.AddChildNode(new FNodeValue(rnd, new Cell(0)));
            rnd.AddChildNode(new FNodeValue(rnd, new Cell(Clusters)));
            FNodeSet keys = new FNodeSet();
            keys.Add(rnd);

            RecordSet rs = AggregatePlan.Render(Data, Where, keys, set);
            return rs;

        }

    }

    public sealed class RowClusterInitializerSpectrum : RowClusterInitializer
    {

        public RowClusterInitializerSpectrum()
            : base()
        {
        }

        public override RecordSet Initialize(DataSet Data, Predicate Where, FNodeSet Fields, int Clusters)
        {

            // Get the min of each field //
            AggregateSet set1 = new AggregateSet();
            for (int i = 0; i < Fields.Count; i++)
            {
                set1.Add(new AggregateMin(Fields[i].CloneOfMe()), Fields.Alias(i));
            }

            // Get the max of each field //
            AggregateSet set2 = new AggregateSet();
            for (int i = 0; i < Fields.Count; i++)
            {
                set2.Add(new AggregateMax(Fields[i].CloneOfMe()), Fields.Alias(i));
            }

            // Render the min and max //
            RecordSet rs1 = AggregatePlan.Render(Data, Where, new FNodeSet(), set1);
            RecordSet rs2 = AggregatePlan.Render(Data, Where, new FNodeSet(), set2);

            // Create the output means table //
            RecordSet rs = new RecordSet(Schema.Join(new Schema("key int"), rs1.Columns));

            // Fill in the gaps //
            for (int i = 0; i < Clusters; i++)
            {

                if (i == 0)
                {
                    RecordBuilder rb = new RecordBuilder();
                    rb.Add(0);
                    rb.Add(rs1[0]);
                    rs.Add(rb.ToRecord());
                }
                else if (i == Clusters - 1)
                {
                    RecordBuilder rb = new RecordBuilder();
                    rb.Add(Clusters - 1);
                    rb.Add(rs2[0]);
                    rs.Add(rb.ToRecord());
                }
                else
                {

                    RecordBuilder rb = new RecordBuilder();
                    rb.Add(i);
                    for (int j = 0; j < rs1.Columns.Count; j++)
                    {
                        double clus = (double)Clusters;
                        double jay = (double)j;
                        rb.Add(rs1[0][j].DOUBLE + (rs2[0][j].DOUBLE - rs1[0][j].DOUBLE) / clus * jay);
                    }
                    rs.Add(rb.ToRecord());

                }

            }

            return rs;

        }

    }

    public sealed class RowClusterCellFunction : CellFunction
    {

        private RowClusterRule _rule;
        private RecordSet _means;

        public RowClusterCellFunction(RowClusterRule Rule, RecordSet Means)
            : base("row_cluster", -1, null, CellAffinity.INT)
        {
            this._rule = Rule;
            this._means = Means;
        }

        public override Cell Evaluate(params Cell[] Data)
        {
            int idx = this._rule.NearestNeighbor(Data, this._means);
            return new Cell(idx);
        }

    }


}
