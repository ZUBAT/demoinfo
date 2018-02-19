﻿using DemoInfo.DP.Handler;
using DemoInfo.DT;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DemoInfo.DP
{
	internal class Entity
	{
		public int ID { get; set; }

		public ServerClass ServerClass { get; set; }

		public PropertyEntry[] Props { get; private set; }

		/// <summary>
		/// Raised after the initial property update after entity is created
		/// </summary>
		internal event Action OnInit;

		public Entity(int id, ServerClass serverClass)
		{
			this.ID = id;
			this.ServerClass = serverClass;

			var flattenedProps = ServerClass.FlattenedProps;
			Props = new PropertyEntry[flattenedProps.Count];
			for (int i = 0; i < flattenedProps.Count; i++)
				Props[i] = new PropertyEntry(flattenedProps[i], i);
		}

		public PropertyEntry FindProperty(string name)
		{
			return Props.Single(a => a.Entry.PropertyName == name);
		}

		/// <summary>
		/// Applies the update.
		/// </summary>
		/// <param name="reader">Reader.</param>
		public void ApplyUpdate(IBitStream reader)
		{
			//Okay, how does an entity-update look like?
			//First a list of the updated props is sent
			//And then the props itself are sent.


			//Read the field-indicies in a "new" way?
			bool newWay = reader.ReadBit();
			int index = -1;
			var entries = new List<PropertyEntry>();

			//No read them. 
			while ((index = ReadFieldIndex(reader, index, newWay)) != -1)
				entries.Add(this.Props[index]);

			//Now read the updated props
			foreach (var prop in entries) {
				prop.Decode(reader, this);
			}
		}

		int ReadFieldIndex(IBitStream reader, int lastIndex, bool bNewWay)
		{
			if (bNewWay) {
				if (reader.ReadBit()) {
					return lastIndex + 1;
				}
			}

			int ret = 0;
			if (bNewWay && reader.ReadBit()) {
				ret = (int)reader.ReadInt(3);  // read 3 bits
			} else {
				ret = (int)reader.ReadInt(7); // read 7 bits
				switch (ret & ( 32 | 64 )) {
				case 32:
					ret = ( ret & ~96 ) | ( (int)reader.ReadInt(2) << 5 );
					break;
				case 64:
					ret = ( ret & ~96 ) | ( (int)reader.ReadInt(4) << 5 );
					break;
				case 96:
					ret = ( ret & ~96 ) | ( (int)reader.ReadInt(7) << 5 );
					break;
				}
			}

			if (ret == 0xFFF) { // end marker is 4095 for cs:go
				return -1;
			}

			return lastIndex + 1 + ret;
		}

		public void Leave ()
		{
			foreach (var prop in Props)
				prop.Destroy ();


		}

		public override string ToString()
		{
			return ID + ": " + this.ServerClass;
		}

		internal void RaiseOnInit()
		{
			if (OnInit != null)
				OnInit();
		}
	}

	class PropertyEntry
	{
		public readonly int Index;
		public FlattenedPropEntry Entry { get; private set; }

		public event EventHandler<PropertyUpdateEventArgs<int>> IntRecived;
		public event EventHandler<PropertyUpdateEventArgs<float>> FloatRecived;
		public event EventHandler<PropertyUpdateEventArgs<Vector>>  VectorRecived;
		public event EventHandler<PropertyUpdateEventArgs<string>>  StringRecived;
		public event EventHandler<PropertyUpdateEventArgs<object[]>>  ArrayRecived;

		#if SAVE_PROP_VALUES
		public object Value { get; private set; }
		#endif

		/*
		 * DON'T USE THIS.
		 * SERIOUSLY, NO!
		 * THERE IS ONLY _ONE_ PATTERN WHERE THIS IS OKAY.
		 *
		 * SendTableParser.FindByName("CBaseTrigger").OnNewEntity += (s1, newResource) => {
		 * 
				Dictionary<string, object> values = new Dictionary<string, object>();
				foreach(var res in newResource.Entity.Props)
				{
					res.DataRecived += (sender, e) => values[e.Property.Entry.PropertyName] = e.Value;
				}
				
		 *
		 * The single purpose for this is to see what kind of values an entity has. You can check this faster with this thing.
		 * Really, ignore it if you don't know what you're doing.
		 */
		[Obsolete("Don't use this attribute. It is only avaible for debugging. Bind to the correct event instead."
		#if !DEBUG
			, true
		#endif
			)]
		#pragma warning disable 0067 // this is unused in release builds, just as it should be
		public event EventHandler<PropertyUpdateEventArgs<object>>  DataRecivedDontUse;
		#pragma warning restore 0067

		[Conditional("DEBUG")]
		private void FireDataReceived_DebugEvent(object val, Entity e)
		{
			#if DEBUG
			#pragma warning disable 0618
			if (DataRecivedDontUse != null)
				DataRecivedDontUse(this, new PropertyUpdateEventArgs<object>(val, e, this));
			#pragma warning restore 0618
			#endif
		}


		[Conditional("DEBUG")]
		private void DeleteDataRecived()
		{
			#if DEBUG
			#pragma warning disable 0618
			DataRecivedDontUse = null;
			#pragma warning restore 0618
			#endif
		}

		public void Decode(IBitStream stream, Entity e)
		{
			//I found no better place for this, sorry.
			//This checks, when in Debug-Mode
			//whether you've bound to the right event
			//Helps finding bugs, where you'd simply miss an update
			CheckBindings(e);

			//So here you start decoding. If you really want 
			//to implement this yourself, GOOD LUCK. 
			//also, be warned: They have 11 ways to read floats. 
			//oh, btw: You may want to read the original Valve-code for this. 
			switch (Entry.Prop.Type) {
			case SendPropertyType.Int:
				{
					var val = PropDecoder.DecodeInt(Entry.Prop, stream);
					if (IntRecived != null)
						IntRecived(this, new PropertyUpdateEventArgs<int>(val, e, this));

					SaveValue (val);
					FireDataReceived_DebugEvent(val, e);
				}
				break;
			case SendPropertyType.Float:
				{
					var val = PropDecoder.DecodeFloat(Entry.Prop, stream);
					if (FloatRecived != null)
						FloatRecived(this, new PropertyUpdateEventArgs<float>(val, e, this));

					SaveValue (val);
					FireDataReceived_DebugEvent(val, e);
				}
				break;
			case SendPropertyType.Vector:
				{
					var val = PropDecoder.DecodeVector(Entry.Prop, stream);
					if (VectorRecived != null)
						VectorRecived(this, new PropertyUpdateEventArgs<Vector>(val, e, this));

					SaveValue (val);
					FireDataReceived_DebugEvent(val, e);
				}
				break;
			case SendPropertyType.Array:
				{
					var val = PropDecoder.DecodeArray(Entry, stream);
					if (ArrayRecived != null)
						ArrayRecived(this, new PropertyUpdateEventArgs<object[]>(val, e, this));

					SaveValue (val);
					FireDataReceived_DebugEvent(val, e);
				}
				break;
			case SendPropertyType.String:
				{
					var val = PropDecoder.DecodeString(Entry.Prop, stream);
					if (StringRecived != null)
						StringRecived(this, new PropertyUpdateEventArgs<string>(val, e, this));

					SaveValue (val);
					FireDataReceived_DebugEvent(val, e);
				}
				break;
			case SendPropertyType.VectorXY:
				{
					var val = PropDecoder.DecodeVectorXY(Entry.Prop, stream);
					if (VectorRecived != null)
						VectorRecived(this, new PropertyUpdateEventArgs<Vector>(val, e, this));

					SaveValue (val);
					FireDataReceived_DebugEvent(val, e);
				}
				break;
			default:
				throw new NotImplementedException("Could not read property. Abort! ABORT! (is it a long?)");
			}

		}

		public PropertyEntry(FlattenedPropEntry prop, int index)
		{
			this.Entry = new FlattenedPropEntry(prop.PropertyName, prop.Prop, prop.ArrayElementProp);
			this.Index = index;
		}

		public void Destroy()
		{
			this.IntRecived = null;
			this.FloatRecived = null;
			this.ArrayRecived = null;
			this.StringRecived = null;
			this.VectorRecived = null;

			DeleteDataRecived ();
		}

		[Conditional("SAVE_PROP_VALUES")]
		private void SaveValue(object value)
		{
			#if SAVE_PROP_VALUES
			this.Value = value;
			#endif
		}

		public override string ToString()
		{
			return string.Format("[PropertyEntry: Entry={0}]", Entry);
		}

		[Conditional("DEBUG")]
		public void CheckBindings(Entity e)
		{
			if (IntRecived != null && this.Entry.Prop.Type != SendPropertyType.Int)
				throw new InvalidOperationException(
					string.Format("({0}).({1}) isn't an {2}", 
						e.ServerClass.Name, 
						Entry.PropertyName, 
						SendPropertyType.Int));

			if (FloatRecived != null && this.Entry.Prop.Type != SendPropertyType.Float)
				throw new InvalidOperationException(
					string.Format("({0}).({1}) isn't an {2}", 
						e.ServerClass.Name, 
						Entry.PropertyName, 
						SendPropertyType.Float));

			if (StringRecived != null && this.Entry.Prop.Type != SendPropertyType.String)
				throw new InvalidOperationException(
					string.Format("({0}).({1}) isn't an {2}", 
						e.ServerClass.Name, 
						Entry.PropertyName, 
						SendPropertyType.String));

			if (ArrayRecived != null && this.Entry.Prop.Type != SendPropertyType.Array)
				throw new InvalidOperationException(
					string.Format("({0}).({1}) isn't an {2}", 
						e.ServerClass.Name, 
						Entry.PropertyName, 
						SendPropertyType.Array));

			if (VectorRecived != null && (this.Entry.Prop.Type != SendPropertyType.Vector && this.Entry.Prop.Type != SendPropertyType.VectorXY))
				throw new InvalidOperationException(
					string.Format("({0}).({1}) isn't an {2}", 
						e.ServerClass.Name, 
						Entry.PropertyName, 
						SendPropertyType.Vector));


		}

		public static void Emit(Entity entity, object[] captured)
		{
			foreach (var arg in captured) {
				var intReceived = arg as RecordedPropertyUpdate<int>;
				var floatReceived = arg as RecordedPropertyUpdate<float>;
				var vectorReceived = arg as RecordedPropertyUpdate<Vector>;
				var stringReceived = arg as RecordedPropertyUpdate<string>;
				var arrayReceived = arg as RecordedPropertyUpdate<object[]>;

				if (intReceived != null) {
					var e = entity.Props[intReceived.PropIndex].IntRecived;
					if (e != null)
						e(null, new PropertyUpdateEventArgs<int>(intReceived.Value, entity, entity.Props[intReceived.PropIndex]));
				} else if (floatReceived != null) {
					var e = entity.Props[floatReceived.PropIndex].FloatRecived;
					if (e != null)
						e(null, new PropertyUpdateEventArgs<float>(floatReceived.Value, entity, entity.Props[floatReceived.PropIndex]));
				} else if (vectorReceived != null) {
					var e = entity.Props[vectorReceived.PropIndex].VectorRecived;
					if (e != null)
						e(null, new PropertyUpdateEventArgs<Vector>(vectorReceived.Value, entity, entity.Props[vectorReceived.PropIndex]));
				} else if (stringReceived != null) {
					var e = entity.Props[stringReceived.PropIndex].StringRecived;
					if (e != null)
						e(null, new PropertyUpdateEventArgs<string>(stringReceived.Value, entity, entity.Props[stringReceived.PropIndex]));
				} else if (arrayReceived != null) {
					var e = entity.Props[arrayReceived.PropIndex].ArrayRecived;
					if (e != null)
						e(null, new PropertyUpdateEventArgs<object[]>(arrayReceived.Value, entity, entity.Props[arrayReceived.PropIndex]));
				} else
					throw new NotImplementedException();
			}
		}
	}

	internal class OwnedEntity
	{
		virtual internal int? EntityID { get; set; }
		virtual internal Player Owner { get; set; }

		protected DemoParser parser;
		protected Entity entity;

		public OwnedEntity(DemoParser _parser)
		{
			parser = _parser;
		}
		public OwnedEntity(Entity ent, DemoParser _parser)
		{
			parser = _parser;
			EntityID = ent.ID;
			entity = ent;
			subToProps(ent);
		}

		internal virtual void subToProps(Entity ent)
		{
			ent.FindProperty("m_hOwnerEntity").IntRecived += (s, handleID) =>
			{
				int playerEntityID = handleID.Value & DemoParser.INDEX_MASK;
				var infos = parser.PlayerInformations;
				if (playerEntityID < infos.Length && infos[playerEntityID - 1] != null)
					Owner = infos[playerEntityID - 1];
			};
		}
	}

	internal class PositionedEntity : OwnedEntity
	{
		virtual internal Vector Origin { get; set; }
		internal int CellX { get; set; }
		internal int CellY { get; set; }
		internal int CellZ { get; set; }

		public PositionedEntity(DemoParser parser) : base(parser) { }
		public PositionedEntity(Entity ent, DemoParser parser) : base(ent, parser)
		{
			subToProps(ent);
		}

		internal override void subToProps(Entity ent)
		{
			base.subToProps(ent);
			ent.FindProperty("m_cellX").IntRecived += (s2, cell) => CellX = cell.Value;
			ent.FindProperty("m_cellY").IntRecived += (s2, cell) => CellY = cell.Value;
			ent.FindProperty("m_cellZ").IntRecived += (s2, cell) => CellZ = cell.Value;
			ent.FindProperty("m_vecOrigin").VectorRecived += (s2, vector) => Origin = vector.Value;
		}

		internal Vector Position
		{
			get
			{
				if (Origin != null)
					return parser.CellsToCoords(CellX, CellY, CellZ) + Origin;
				else
					return new Vector();
			}
		}
	}

	internal enum BombState { Held, Planting, Planted, Defusing, Defused, Exploded };

	internal class BombEntity : PositionedEntity
	{
		internal Player Defuser;
		internal int ExplodeTick;
		internal bool Defused { get { return BombState == BombState.Defused; } }
		internal char Site {
			get
			{
				double distToA = Position.Distance(parser.bombsiteACenter);
				double distToB = Position.Distance(parser.bombsiteBCenter);
				return distToA < distToB ? 'A' : 'B';
			}
		}
		private BombState _bombState = BombState.Held;
		internal BombState BombState {
			get { return _bombState; }
			set
			{
				_bombState = value;
				if (value == BombState.Exploded)
					ExplodeTick = parser.IngameTick;
			}
		}

		internal BombEntity(Entity ent, DemoParser parser) : base(ent, parser)
		{
			BombState = BombState.Held;
		}

		internal BombEventArgs MakeBombArgs()
		{
			var args = new BombEventArgs();
			args.Player = Owner;
			args.Site = Site;

			return args;
		}
	}

	enum DetonateState { PreDetonate, Detonating, Detonated }

	abstract class DetonateEntity : PositionedEntity
	{
		internal NadeEventArgs NadeArgs = new NadeEventArgs();
		internal DetonateState DetonateState = DetonateState.PreDetonate;

		// Necessary to use properties here so that NadeArgs is kept current
		override internal int? EntityID { get { return NadeArgs.EntityID; } set { NadeArgs.EntityID = value; } }
		override internal Vector Origin
		{
			get { return _origin; }
			set
			{ // origin is always present in position updates
				_origin = value;
				if (NadeArgs.Interpolated)
					NadeArgs.Position = Position;
			}
		}
		override internal Player Owner { get { return NadeArgs.ThrownBy; } set { NadeArgs.ThrownBy = value; } }

		Vector _origin;

		internal DetonateEntity(DemoParser parser) : base(parser) { }
		internal DetonateEntity(Entity ent, DemoParser parser) : base(ent, parser)
		{
		}

		abstract internal void RaiseNadeStart();
		abstract internal void RaiseNadeEnd();
		abstract internal void CopyAndReplaceNadeArgs(); // so that same args aren't raised for start and end
	}

	class FireDetonateEntity : DetonateEntity
	{
		internal FireDetonateEntity(DemoParser parser) : base(parser) { }
		internal FireDetonateEntity(Entity ent, DemoParser parser) : base(ent, parser)
		{
			NadeArgs = new FireEventArgs(NadeArgs);
			NadeArgs.Interpolated = true;
		}

		internal override void RaiseNadeStart()
		{
			parser.RaiseFireWithOwnerStart((FireEventArgs)NadeArgs);
			DetonateState = DetonateState.Detonating;
		}

		internal override void RaiseNadeEnd()
		{
			parser.RaiseFireEnd((FireEventArgs)NadeArgs);
		}

		internal override void CopyAndReplaceNadeArgs()
		{
			NadeArgs = new FireEventArgs(NadeArgs);
		}
	}

	class SmokeDetonateEntity : DetonateEntity
	{
		internal SmokeDetonateEntity(Entity ent, DemoParser parser) : base(ent, parser)
		{
			NadeArgs = new SmokeEventArgs(NadeArgs);
			NadeArgs.Interpolated = true;
		}

		internal override void RaiseNadeStart()
		{
			parser.RaiseSmokeStart((SmokeEventArgs)NadeArgs);
			DetonateState = DetonateState.Detonating;
		}

		internal override void RaiseNadeEnd()
		{
			parser.RaiseSmokeEnd((SmokeEventArgs)NadeArgs);
		}

		internal override void CopyAndReplaceNadeArgs()
		{
			NadeArgs = new SmokeEventArgs(NadeArgs);
		}
	}

	class DecoyDetonateEntity : DetonateEntity
	{
		internal float? FlagTime;

		internal DecoyDetonateEntity(Entity ent, DemoParser parser) : base(ent, parser)
		{
			NadeArgs = new DecoyEventArgs(NadeArgs);
			NadeArgs.Interpolated = true;

			ent.FindProperty("m_fFlags").IntRecived += (s, flag) =>
			{
				// There doesn't seem to be any property that is tightly coupled with
				// decoy_started events, but m_fFlags always occurs some time beforehand.
				if (flag.Value == 1)
				{
					if (DetonateState == DetonateState.PreDetonate)
					{
						// It's possible, but rare, for m_fFlags to be set on the same tick as decoy_started
						FlagTime = parser.CurrentTime;
					}
				}
			};
		}

		internal override void RaiseNadeStart()
		{
			parser.RaiseDecoyStart((DecoyEventArgs)NadeArgs);
			DetonateState = DetonateState.Detonating;
		}

		internal override void RaiseNadeEnd()
		{
			parser.RaiseDecoyEnd((DecoyEventArgs)NadeArgs);
		}

		internal override void CopyAndReplaceNadeArgs()
		{
			NadeArgs = new DecoyEventArgs(NadeArgs);
		}
	}

	class BaseProjectileEntity : DetonateEntity
	{
		// The same entity is used for both HEs and Flashes
		// But we need to be able to use data from before we can determine which type it is

		internal float DmgRadius;
		const float HE_DMGRADIUS = 350; // Just have to hope 350 remains the default, but there's no guarantee
		bool isHE;

		internal BaseProjectileEntity(Entity ent, DemoParser parser) : base(ent, parser)
		{
			NadeArgs = new FlashEventArgs(NadeArgs);
			NadeArgs.Interpolated = true;

			ent.FindProperty("m_DmgRadius").FloatRecived += (s, dmg) => DmgRadius = dmg.Value;

			setIsHE();
		}

		internal override void RaiseNadeStart()
		{

		}

		internal override void RaiseNadeEnd()
		{
			if (isHE)
				parser.RaiseGrenadeExploded((GrenadeEventArgs)NadeArgs);
			else
				parser.RaiseFlashExploded((FlashEventArgs)NadeArgs);

			DetonateState = DetonateState.Detonated;
		}

		internal override void CopyAndReplaceNadeArgs()
		{
			if (isHE)
				NadeArgs = new GrenadeEventArgs(NadeArgs);
			else
				NadeArgs = new FlashEventArgs(NadeArgs);
		}

		internal void setIsHE()
		{
			EventHandler<EventArgs> lambda = null;
			lambda = (s2, ee) =>
			{
				isHE = DmgRadius == HE_DMGRADIUS;

				if (isHE)
					NadeArgs = new GrenadeEventArgs(NadeArgs);

				parser.PreTickDone -= lambda;
			};

			parser.PreTickDone += lambda;
		}
	}

	#region Update-Types
	class PropertyUpdateEventArgs<T> : EventArgs
	{
		public T Value { get; private set; }

		public Entity Entity { get; private set; }

		public PropertyEntry Property { get; private set; }

		public PropertyUpdateEventArgs(T value, Entity e, PropertyEntry p)
		{
			this.Value = value;
			this.Entity = e;
			this.Property = p;
		}
	}

	public class RecordedPropertyUpdate<T>
	{
		public int PropIndex;
		public T Value;

		public RecordedPropertyUpdate (int propIndex, T value)
		{
			PropIndex = propIndex;
			Value = value;
		}
	}
	#endregion
}
