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
using System.Diagnostics;
using System.Threading;
using System.Text;
using System.Runtime.InteropServices;
namespace LibreLancer
{
	public class Game
	{
		int width;
		int height;
		double totalTime;
		bool fullscreen;
		bool running = false;
		string title = "LibreLancer";
		IntPtr windowptr;
		double renderFrequency;
		public Mouse Mouse = new Mouse();
		public Keyboard Keyboard = new Keyboard();
		public Game (int w, int h, bool fullscreen)
		{
			width = w;
			height = h;
		}

		public int Width {
			get {
				return width;
			}
		}

		public int Height {
			get {
				return height;
			}
		}

		public double TotalTime {
			get {
				return totalTime;
			}
		}

		public string Title {
			get {
				return title;
			} set {
				title = value;
				SDL.SDL_SetWindowTitle (windowptr, title);
			}
		}

		public double RenderFrequency {
			get {
				return renderFrequency;
			}
		}

		public void Run()
		{
			if (SDL.SDL_Init (SDL.SDL_INIT_VIDEO) != 0) {
				FLLog.Error ("SDL", "SDL_Init failed, exiting.");
				return;
			}
			//Set GL states
			SDL.SDL_GL_SetAttribute (SDL.SDL_GLattr.SDL_GL_CONTEXT_MAJOR_VERSION, 3);
			SDL.SDL_GL_SetAttribute (SDL.SDL_GLattr.SDL_GL_CONTEXT_MINOR_VERSION, 2);
			SDL.SDL_GL_SetAttribute (SDL.SDL_GLattr.SDL_GL_CONTEXT_PROFILE_MASK, (int)SDL.SDL_GLprofile.SDL_GL_CONTEXT_PROFILE_CORE);
			//Create Window
			var flags = SDL.SDL_WindowFlags.SDL_WINDOW_OPENGL | SDL.SDL_WindowFlags.SDL_WINDOW_RESIZABLE;
			if (fullscreen)
				flags |= SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP;
			var sdlWin = SDL.SDL_CreateWindow (
				             "LibreLancer",
				             SDL.SDL_WINDOWPOS_CENTERED,
				             SDL.SDL_WINDOWPOS_CENTERED,
				             width,
				             height,
				             flags
			             );
			if (sdlWin == IntPtr.Zero) {
				FLLog.Error ("SDL", "Failed to create window, exiting.");
				return;
			}
			windowptr = sdlWin;
			var glcontext = SDL.SDL_GL_CreateContext (sdlWin);
			if (glcontext == IntPtr.Zero) {
				FLLog.Error ("OpenGL", "Failed to create OpenGL context, exiting.");
				return;
			}
			//Load pointers
			GL.Load();
			//Init game state
			Load();
			//Start game
			running = true;
			var timer = new Stopwatch ();
			timer.Start ();
			double last = 0;
			double elapsed = 0;
			SDL.SDL_Event e;
			while (running) {
				//Pump message queue
				while (SDL.SDL_PollEvent (out e) != 0) {
					switch (e.type) {
					case SDL.SDL_EventType.SDL_QUIT:
						{
							running = false; //TODO: Raise Event
							break;
						}
					case SDL.SDL_EventType.SDL_MOUSEMOTION:
						{
							Mouse.X = e.motion.x;
							Mouse.Y = e.motion.y;
							Mouse.OnMouseMove ();
							break;
						}
					case SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN:
						{
							Mouse.X = e.button.x;
							Mouse.Y = e.button.y;
							var btn = GetMouseButton (e.button.button);
							Mouse.Buttons |= btn;
							Mouse.OnMouseDown (btn);
							break;
						}
					case SDL.SDL_EventType.SDL_MOUSEBUTTONUP:
						{
							Mouse.X = e.button.x;
							Mouse.Y = e.button.y;
							var btn = GetMouseButton (e.button.button);
							Mouse.Buttons &= ~btn;
							Mouse.OnMouseUp (btn);
							break;
						}
					case SDL.SDL_EventType.SDL_MOUSEWHEEL:
						{
							Mouse.OnMouseWheel (e.wheel.y);
							break;
						}
					case SDL.SDL_EventType.SDL_TEXTINPUT:
						{
							Keyboard.OnTextInput (GetEventText (ref e));
							break;
						}
					case SDL.SDL_EventType.SDL_KEYDOWN:
						{
							Keyboard.OnKeyDown ((Keys)e.key.keysym.sym, (KeyModifiers)e.key.keysym.mod);
							break;
						}
					case SDL.SDL_EventType.SDL_KEYUP:
						{
							Keyboard.OnKeyUp ((Keys)e.key.keysym.sym, (KeyModifiers)e.key.keysym.mod);
							break;
						}
					}
				}
				//Do game things
				if (!running)
					break;
				Update (elapsed);
				if (!running)
					break;
				Draw (elapsed);
				elapsed = timer.Elapsed.TotalSeconds - last;
				last = timer.Elapsed.TotalSeconds;
				totalTime = timer.Elapsed.TotalSeconds;
				SDL.SDL_GL_SwapWindow (sdlWin);
				if (elapsed < 0) {
					elapsed = 0;
					FLLog.Warning ("Timing", "Stopwatch returned negative time!");
				}
				Thread.Sleep (0);
			}
			Cleanup ();
			SDL.SDL_Quit ();
		}

		//Convert from SDL2 button to saner button
		MouseButtons GetMouseButton(byte b)
		{
			if (b == SDL.SDL_BUTTON_LEFT)
				return MouseButtons.Left;
			if (b == SDL.SDL_BUTTON_MIDDLE)
				return MouseButtons.Middle;
			if (b == SDL.SDL_BUTTON_RIGHT)
				return MouseButtons.Right;
			if (b == SDL.SDL_BUTTON_X1)
				return MouseButtons.X1;
			if (b == SDL.SDL_BUTTON_X2)
				return MouseButtons.X2;
			throw new Exception ("SDL2 gave undefined mouse button"); //should never happen
		}

		unsafe string GetEventText(ref SDL.SDL_Event e)
		{
			byte[] rawBytes = new byte[SDL.SDL_TEXTINPUTEVENT_TEXT_SIZE];
			fixed (byte* txtPtr = e.text.text) {
				Marshal.Copy ((IntPtr)txtPtr, rawBytes, 0, SDL.SDL_TEXTINPUTEVENT_TEXT_SIZE);
			}
			int nullIndex = Array.IndexOf (rawBytes, (byte)0);
			string text = Encoding.UTF8.GetString (rawBytes, 0, nullIndex);
			return text;
		}

		public void Exit()
		{
			running = false;
		}
		protected virtual void Load()
		{

		}
		protected virtual void Update(double elapsed)
		{

		}
		protected virtual void Draw(double elapsed)
		{

		}
		protected virtual void Cleanup()
		{

		}
	}
}

