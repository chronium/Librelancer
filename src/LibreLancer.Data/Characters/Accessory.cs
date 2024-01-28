﻿// MIT License - Copyright (c) Malte Rupprecht
// This file is subject to the terms and conditions defined in
// LICENSE, which is part of this source code package

using System;

using LibreLancer.Ini;

namespace LibreLancer.Data.Characters
{
	public class Accessory
	{
        [Entry("nickname", Required = true)]
        public string Nickname;
        [Entry("mesh")]
        public string Mesh;
        [Entry("hardpoint")]
        public string Hardpoint;
        [Entry("body_hardpoint")]
        public string BodyHardpoint;
	}
}
