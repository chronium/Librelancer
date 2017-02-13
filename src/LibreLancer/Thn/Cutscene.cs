﻿/* The contents of this file are subject to the Mozilla Public License
 * Version 1.1 (the "License"); you may not use this file except in
 * compliance with the License. You may obtain a copy of the License at
 * http://www.mozilla.org/MPL/
 * 
 * Software distributed under the License is distributed on an "AS IS"
 * basis, WITHOUT WARRANTY OF ANY KIND, either express or implied. See the
 * License for the specific language governing rights and limitations
 * under the License.
 * 
 * 
 * The Initial Developer of the Original Code is Callum McGing (mailto:callum.mcging@gmail.com).
 * Portions created by the Initial Developer are Copyright (C) 2013-2016
 * the Initial Developer. All Rights Reserved.
 */
using System;
using System.Collections.Generic;
using LibreLancer.Thorn;

namespace LibreLancer
{
	public class Cutscene
	{
		class ThnObject
		{
			public string Name;
			public Vector3 Translate;
			public Matrix4 Rotate;
			public GameObject Object;
			public DynamicLight Light;
			public ThnEntity Entity;
			public ThnCameraTransform Camera;
		}

		interface IThnRoutine
		{
			bool Run(Cutscene cs, double delta);
		}

		double currentTime = 0;
		Queue<ThnEvent> events = new Queue<ThnEvent>();
		Dictionary<string, ThnObject> objects = new Dictionary<string, ThnObject>(StringComparer.OrdinalIgnoreCase);
		List<IThnRoutine> coroutines = new List<IThnRoutine>();
		ThnScript thn;

		GameWorld world;
		SystemRenderer renderer;

		ThnCamera camera;

		public Cutscene(ThnScript script, FreelancerGame game)
		{
			camera = new ThnCamera(game.Viewport);

			renderer = new SystemRenderer(camera, game.GameData, game.ResourceManager);
			world = new GameWorld(renderer);

			thn = script;
			foreach (var ev in thn.Events)
				events.Enqueue(ev);
			bool hasScene = false;
			List<Tuple<IDrawable, Matrix4, int>> layers = new List<Tuple<IDrawable, Matrix4, int>>();
			foreach (var kv in thn.Entities)
			{
				var obj = new ThnObject();
				obj.Name = kv.Key;
				obj.Translate = kv.Value.Position ?? Vector3.Zero;
				obj.Rotate = kv.Value.RotationMatrix ?? Matrix4.Identity;
				if (kv.Value.Type == EntityTypes.Compound)
				{
					//Fetch model
					IDrawable drawable;
					switch (kv.Value.MeshCategory.ToLowerInvariant())
					{
						case "solar":
							drawable = game.GameData.GetSolar(kv.Value.Template);
							break;
						case "spaceship":
							var sh = game.GameData.GetShip(kv.Value.Template);
							drawable = sh.Drawable;
							break;
						case "prop":
							drawable = game.GameData.GetProp(kv.Value.Template);
							break;
						default:
							throw new NotImplementedException("Mesh Category " + kv.Value.MeshCategory);
					}
					if (kv.Value.UserFlag == 1)
					{
						//This is a starsphere
						layers.Add(new Tuple<IDrawable, Matrix4, int>(drawable, kv.Value.RotationMatrix ?? Matrix4.Zero, kv.Value.SortGroup));
					}
					else
					{
						obj.Object = new GameObject(drawable, game.ResourceManager, false);
						var r = (ModelRenderer)obj.Object.RenderComponent;
						r.LightGroup = kv.Value.LightGroup;
					}
				}
				else if (kv.Value.Type == EntityTypes.PSys)
				{
					var fx = game.GameData.GetEffect(kv.Value.Template);
					obj.Object = new GameObject();
					obj.Object.RenderComponent = new ParticleEffectRenderer(fx) { Active = false };
				}
				else if (kv.Value.Type == EntityTypes.Scene)
				{
					if (hasScene)
						throw new Exception("Thn can only have one scene");
					hasScene = true;
					var amb = kv.Value.Ambient.Value;
					renderer.SystemLighting.Ambient = new Color4(amb.X / 255f, amb.Y / 255f, amb.Z / 255f, 1);
				}
				else if (kv.Value.Type == EntityTypes.Light)
				{
					var lt = new DynamicLight();
					lt.LightGroup = kv.Value.LightGroup;
					lt.Active = kv.Value.LightProps.On;
					lt.Light = kv.Value.LightProps.Render;
					obj.Light = lt;
					if (kv.Value.RotationMatrix.HasValue)
					{
						var m = kv.Value.RotationMatrix.Value;
						lt.Light.Direction = (new Vector4(lt.Light.Direction.Normalized(), 0) * m).Xyz.Normalized();
					}
					renderer.SystemLighting.Lights.Add(lt);
				}
				else if (kv.Value.Type == EntityTypes.Camera)
				{
					obj.Camera = new ThnCameraTransform();
					obj.Camera.Position = kv.Value.Position.Value;
					obj.Camera.Orientation = kv.Value.RotationMatrix ?? Matrix4.Identity;
					obj.Camera.FovH = kv.Value.FovH ?? obj.Camera.FovH;
					obj.Camera.AspectRatio = kv.Value.HVAspect ?? obj.Camera.AspectRatio;
				}
				else if (kv.Value.Type == EntityTypes.Marker)
				{
					obj.Object = new GameObject();
					obj.Object.Name = "Marker";
					obj.Object.Nickname = "";
				}
				if (obj.Object != null)
				{
					Vector3 transform = kv.Value.Position ?? Vector3.Zero;
					obj.Object.Transform = (kv.Value.RotationMatrix ?? Matrix4.Identity) * Matrix4.CreateTranslation(transform);
					world.Objects.Add(obj.Object);
				}
				obj.Entity = kv.Value;
				objects.Add(kv.Key, obj);
			}
			//Add starspheres in the right order
			layers.Sort((x, y) => x.Item3.CompareTo(y.Item3));
			renderer.StarSphereModels = new IDrawable[layers.Count];
			renderer.StarSphereWorlds = new Matrix4[layers.Count];
			for (int i = 0; i < layers.Count; i++)
			{
				renderer.StarSphereModels[i] = layers[i].Item1;
				renderer.StarSphereWorlds[i] = layers[i].Item2;
			}
			//Add objects to the renderer
			world.RegisterAll();
		}

		public void Update(TimeSpan delta)
		{
			currentTime += delta.TotalSeconds;
			for (int i = (coroutines.Count - 1); i >= 0; i--)
			{
				if (!coroutines[i].Run(this, delta.TotalSeconds))
				{
					coroutines.RemoveAt(i);
					i--;
				}
			}
			while (events.Count > 0 && events.Peek().Time <= currentTime)
			{
				var ev = events.Dequeue();
				ProcessEvent(ev);
			}
			camera.Update();
			world.Update(delta);
		}

		public void Draw()
		{
			renderer.Draw();
		}

		void ProcessEvent(ThnEvent ev)
		{
			switch (ev.Type)
			{
				case EventTypes.SetCamera:
					ProcessSetCamera(ev);
					break;
				case EventTypes.AttachEntity:
					ProcessAttachEntity(ev);
					break;
				case EventTypes.StartPSys:
					ProcessStartPSys(ev);
					break;
				case EventTypes.StartMotion:
					ProcessStartMotion(ev);
					break;
				case EventTypes.StartFogPropAnim:
					ProcessStartFogPropAnim(ev);
					break;
				case EventTypes.StartPathAnimation:
					ProcessStartPathAnimation(ev);
					break;
			}
		}

		#region SetCamera
		void ProcessSetCamera(ThnEvent ev)
		{
			var cam = objects[(string)ev.Targets[1]];
			camera.Transform = cam.Camera;
		}
		#endregion

		#region AttachEntity
		class AttachCameraToObject : IThnRoutine
		{
			public float Duration;
			public ThnCameraTransform Camera;
			public Vector3 Offset;
			public GameObject Object;
			public GameObject Part;
			public bool Position;
			public bool Orientation;
			public bool LookAt;
			double t = 0;
			public bool Run(Cutscene cs, double delta)
			{
				Matrix4 transform;
				if (Part != null)
					transform = Part.GetTransform();
				else
					transform = Object.GetTransform();
				t += delta;
				if (t > Duration)
				{
					if (LookAt)
						Camera.LookAt = null;
					return false;
				}
				if (Position)
					Camera.Position = transform.ExtractTranslation();
				if (Orientation)
					Camera.Orientation = Matrix4.CreateFromQuaternion(transform.ExtractRotation());
				return true;
			}
		}

		class DetachObject : IThnRoutine
		{
			public float Duration;
			public GameObject Object;
			double t = 0;
			public bool Run(Cutscene cs, double delta)
			{
				t += delta;
				if (t > Duration)
					return false;

				return true;
			}
		}

		void ProcessAttachEntity(ThnEvent ev)
		{
			object tmp;

			var objA = objects[(string)ev.Targets[0]];
			var objB = objects[(string)ev.Targets[1]];
			var targetType = (TargetTypes)ev.Properties["target_type"];
			var flags = AttachFlags.Position | AttachFlags.Orientation;
			Vector3 offset;

			if (ev.Properties.TryGetValue("flags", out tmp))
				flags = (AttachFlags)tmp;
			ev.Properties.TryGetVector3("offset", out offset);
			//Attach GameObjects to eachother
			if (objA.Object != null && objB.Object != null)
			{
				if (targetType == TargetTypes.Hardpoint)
				{
					var targetHp = ev.Properties["target_part"].ToString();
					var hp = objB.Object.GetHardpoint(targetHp);
					objA.Object.Attachment = hp;
					objA.Object.Parent = objB.Object;
					objA.Object.Transform = Matrix4.CreateTranslation(offset);
				}
				else if (targetType == TargetTypes.Root)
				{
					objA.Object.Transform = Matrix4.CreateTranslation(offset);
					objA.Object.Parent = objB.Object;
				}

			}
			//Attach GameObjects and Cameras to eachother
			if (objA.Object != null && objB.Camera != null)
			{

			}
			if (objA.Camera != null && objB.Object != null)
			{
				if ((flags & AttachFlags.LookAt) == AttachFlags.LookAt)
				{
					objA.Camera.LookAt = objB.Object;
				}
				GameObject part = null;
				if (targetType == TargetTypes.Hardpoint)
				{
					part = new GameObject();
					part.Parent = objB.Object;
					part.Attachment = objB.Object.GetHardpoint(ev.Properties["target_part"].ToString());
				}
				if (targetType == TargetTypes.Part)
				{
					var hp = new Hardpoint(null, part.CmpConstructs.Find(ev.Properties["target_part"].ToString())); //Create a dummy hardpoint to attach to
					part = new GameObject();
					part.Parent = objB.Object;
					part.Attachment = hp;
				}
				coroutines.Add(new AttachCameraToObject()
				{
					Duration = ev.Duration,
					Camera = objA.Camera,
					Object = objB.Object,
					Part = part,
					Position = ((flags & AttachFlags.Position) == AttachFlags.Position),
					Orientation = ((flags & AttachFlags.Orientation) == AttachFlags.Orientation),
					LookAt = ((flags & AttachFlags.LookAt) == AttachFlags.LookAt)
				});
			}
		}
		#endregion

		#region StartFogPropAnim
		class FogPropAnimRoutine : IThnRoutine
		{
			public ThnEvent Event;
			public Vector3? FogColor;
			public float? FogStart;
			public float? FogEnd;
			public float? FogDensity;

			public Color4 OrigFogColor;
			public float OrigFogStart;
			public float OrigFogEnd;
			public float OrigFogDensity;

			double t = 0;
			public bool Run(Cutscene cs, double delta)
			{
				t += delta;
				if (t > Event.Duration)
					return false;

				return true;
			}
		}

		void ProcessStartFogPropAnim(ThnEvent ev)
		{
			//fogmode is ignored.
			//fogdensity is ignored.
			var fogprops = (LuaTable)ev.Properties["fogprops"];

			object tmp;
			Vector3 tmp2;

			//Nullable since we are animating
			bool? fogon = null;
			Vector3? fogColor = null;
			float? fogstart = null;
			float? fogend = null;
			float? fogDensity = null;
			FogModes fogMode = FogModes.Linear;
			//Get values
			if (fogprops.TryGetValue("fogon", out tmp))
				fogon = (bool)tmp;
			if (fogprops.TryGetValue("fogmode", out tmp))
				fogMode = (FogModes)tmp;
			if (fogprops.TryGetValue("fogdensity", out tmp))
				fogDensity = (float)tmp;
			if (fogprops.TryGetVector3("fogcolor", out tmp2))
				fogColor = tmp2;
			if (fogprops.TryGetValue("fogstart", out tmp))
				fogstart = (float)tmp;
			if (fogprops.TryGetValue("fogend", out tmp))
				fogend = (float)tmp;

			if (fogon.HasValue) //i'm pretty sure this can't be animated
				renderer.SystemLighting.FogMode = fogon.Value ? fogMode : FogModes.None;

			//Set fog
			if (Math.Abs(ev.Duration) < float.Epsilon) //just set it
			{
				if (fogColor.HasValue)
				{
					var v = fogColor.Value;
					v *= (1 / 255f);
					renderer.SystemLighting.FogColor = new Color4(v.X, v.Y, v.Z, 1);
				}
				if (fogstart.HasValue)
					renderer.SystemLighting.FogRange.X = fogstart.Value;
				if (fogend.HasValue)
					renderer.SystemLighting.FogRange.Y = fogend.Value;
				if (fogDensity.HasValue)
					renderer.SystemLighting.FogDensity = fogDensity.Value;
			}
			else
				coroutines.Add(new FogPropAnimRoutine() //animate it!
				{
					Event = ev,
					FogDensity = fogDensity,
					FogColor = fogColor,
					FogStart = fogstart,
					FogEnd = fogend,
					OrigFogColor = renderer.SystemLighting.FogColor,
					OrigFogStart = renderer.SystemLighting.FogRange.X,
					OrigFogEnd = renderer.SystemLighting.FogRange.Y,
					OrigFogDensity = renderer.SystemLighting.FogDensity
				});
		}
		#endregion

		#region StartPSys
		void ProcessStartPSys(ThnEvent ev)
		{
			var obj = objects[(string)ev.Targets[0]];
			((ParticleEffectRenderer)obj.Object.RenderComponent).Active = true;
		}
		#endregion

		#region StartMotion
		void ProcessStartMotion(ThnEvent ev)
		{
			//How to tie this in with .anm files?
			var obj = objects[(string)ev.Targets[0]];
			if (obj.Object != null && obj.Object.AnimationComponent != null) //Check if object has Cmp animation
			{
				obj.Object.AnimationComponent.StartAnimation((string)ev.Properties["animation"]);
			}
		}
		#endregion

		static void OrthoNormalize(ref Vector3 normal, ref Vector3 tangent)
		{
			normal.Normalize();
			var proj = normal * Vector3.Dot(tangent, normal);
			tangent = tangent - proj;
			tangent.Normalize();
		}

		static Quaternion LookRotation(Vector3 direction, Vector3 up)
		{
			var forward = direction.Normalized();
			OrthoNormalize(ref up, ref forward);
			var right = Vector3.Cross(up, forward);
			var ret = new Quaternion();
			ret.W = (float)Math.Sqrt(1 + right.X + up.Y + forward.Z) * 0.5f;
			float w4_recip = 1f / (4f * ret.W);
			ret.X = (up.Z - forward.Y) * w4_recip;
			ret.Y = (forward.X - right.Z) * w4_recip;
			ret.Z = (right.Y - up.X) * w4_recip;
			return ret;
		}

		#region StartPathAnimation
		abstract class PathAnimationBase : IThnRoutine
		{
			public float Duration;
			public float StartPercent;
			public float StopPercent;
			public AttachFlags Flags;
			public ParameterCurve Curve;
			public ThnObject Path;


			double time = 0;

			public bool Run(Cutscene cs, double delta)
			{
				time += delta;
				if (time > Duration)
				{
					if (Curve != null)
						Process(Curve.GetValue(Duration, Duration));
					else
						Process(1);
					return false;
				}
				if (Curve != null)
					Process(Curve.GetValue((float)time, Duration));
				else
					Process((float)time / Duration);
				return true;
			}

			void Process(float t)
			{
				float pct = MathHelper.Lerp(StartPercent, StopPercent, t);
				var path = Path.Entity.Path;
				var pos = path.GetPosition(pct);
				if ((Flags & AttachFlags.LookAt) == AttachFlags.LookAt)
				{
					var orient = Matrix4.CreateFromQuaternion(LookRotation(path.GetDirection(pct), Vector3.UnitY));
					if ((Flags & AttachFlags.Position) == AttachFlags.Position)
						SetPositionOrientation(pos + Path.Translate, orient);
					else
						SetOrientation(orient);
				}
				else if ((Flags & AttachFlags.Orientation) == AttachFlags.Orientation)
				{

				}
				else if ((Flags & AttachFlags.Position) == AttachFlags.Position)
				{
					SetPosition(pos + Path.Translate);
				}
			}

			protected abstract void SetPosition(Vector3 pos);
			protected abstract void SetPositionOrientation(Vector3 pos, Matrix4 orient);
			protected abstract void SetOrientation(Matrix4 orient);
		}

		class ObjectPathAnimation : PathAnimationBase
		{
			public GameObject Object;

			protected override void SetPosition(Vector3 pos)
			{
				var rot = Object.Transform.ExtractRotation();
				Object.Transform = Matrix4.CreateFromQuaternion(rot) * Matrix4.CreateTranslation(pos);
			}
			protected override void SetPositionOrientation(Vector3 pos, Matrix4 orient)
			{
				Object.Transform = orient * Matrix4.CreateTranslation(pos);
			}
			protected override void SetOrientation(Matrix4 orient)
			{
				var translation = Object.Transform.ExtractTranslation();
				Object.Transform = orient * Matrix4.CreateTranslation(translation);
			}
		}

		class CameraPathAnimation : PathAnimationBase
		{
			public ThnCameraTransform Camera;

			protected override void SetPosition(Vector3 pos)
			{
				Camera.Position = pos;
			}
			protected override void SetPositionOrientation(Vector3 pos, Matrix4 orient)
			{
				Camera.Position = pos;
				Camera.Orientation = orient;
			}
			protected override void SetOrientation(Matrix4 orient)
			{
				Camera.Orientation = orient;
			}
		}

		void ProcessStartPathAnimation(ThnEvent ev)
		{
			var obj = objects[(string)ev.Targets[0]];
			var path = objects[(string)ev.Targets[1]];
			var start = (float)ev.Properties["start_percent"];
			var stop = (float)ev.Properties["stop_percent"];
			var flags = (AttachFlags)ev.Properties["flags"];
			if (obj.Object != null)
			{
				coroutines.Add(new ObjectPathAnimation()
				{
					Duration = ev.Duration,
					StartPercent = start,
					StopPercent = stop,
					Flags = flags,
					Curve = ev.ParamCurve,
					Path = path,
					Object = obj.Object
				});
			}
			if (obj.Camera != null)
			{
				coroutines.Add(new CameraPathAnimation()
				{
					Duration = ev.Duration,
					StartPercent = start,
					StopPercent = stop,
					Flags = flags,
					Curve = ev.ParamCurve,
					Path = path,
					Camera = obj.Camera
				});
			}
		}
		#endregion
	}
}

