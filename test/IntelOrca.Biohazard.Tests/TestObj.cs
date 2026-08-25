using System;
using System.Globalization;
using System.IO;
using IntelOrca.Biohazard.Model;
using Xunit;

namespace IntelOrca.Biohazard.Tests
{
    public class TestObj
    {
        [Fact]
        public void ExportUsesDotDecimalSeparatorUnderCommaLocale()
        {
            var objPath = Path.Combine(Path.GetTempPath(), $"biohazard-utils-{Guid.NewGuid():N}.obj");
            try
            {
                WithCommaDecimalCulture(() =>
                {
                    new ObjExporter().Export(CreateMesh(), objPath, 1);

                    var text = File.ReadAllText(objPath);
                    Assert.DoesNotContain(",", text);
                    Assert.Contains("v 0.510000 -0.487000 0.238000", text);
                    Assert.Contains("vn 0.020000 -0.040000 0.060000", text);
                    Assert.Contains("vt 0.500000 0.750000", text);

                    var obj = new WavefrontObjFile(objPath);
                    Assert.Single(obj.Objects);
                    Assert.Single(obj.Objects[0].Triangles);
                    Assert.Equal(3, obj.Vertices.Count);
                    Assert.Equal(0.51, obj.Vertices[0].x, 9);
                    Assert.Equal(-0.487, obj.Vertices[0].y, 9);
                    Assert.Equal(0.238, obj.Vertices[0].z, 9);
                    Assert.Equal(0.02, obj.Normals[0].x, 9);
                    Assert.Equal(-0.04, obj.Normals[0].y, 9);
                    Assert.Equal(0.06, obj.Normals[0].z, 9);
                    Assert.Equal(0.5, obj.TextureCoordinates[0].u, 9);
                    Assert.Equal(0.75, obj.TextureCoordinates[0].v, 9);
                });
            }
            finally
            {
                TryDelete(objPath);
                TryDelete(Path.ChangeExtension(objPath, ".mtl"));
            }
        }

        [Fact]
        public void ImportParsesDotDecimalsUnderCommaLocale()
        {
            var objPath = Path.Combine(Path.GetTempPath(), $"biohazard-utils-{Guid.NewGuid():N}.obj");
            try
            {
                File.WriteAllText(objPath,
                    "o part_00\n" +
                    "v 0.510000 -0.487000 0.238000\n" +
                    "vn 0.020000 -0.040000 0.060000\n" +
                    "vt 0.500000 0.750000\n" +
                    "f 1/1/1 2/2/2 3/3/3\n");

                WithCommaDecimalCulture(() =>
                {
                    var obj = new WavefrontObjFile(objPath);
                    Assert.Equal(0.51, obj.Vertices[0].x, 9);
                    Assert.Equal(-0.487, obj.Vertices[0].y, 9);
                    Assert.Equal(0.238, obj.Vertices[0].z, 9);
                    Assert.Equal(0.02, obj.Normals[0].x, 9);
                    Assert.Equal(-0.04, obj.Normals[0].y, 9);
                    Assert.Equal(0.06, obj.Normals[0].z, 9);
                    Assert.Equal(0.5, obj.TextureCoordinates[0].u, 9);
                    Assert.Equal(0.75, obj.TextureCoordinates[0].v, 9);
                    Assert.Equal(0, obj.Objects[0].Triangles[0].a.Vertex);
                    Assert.Equal(2, obj.Objects[0].Triangles[0].c.Vertex);
                });
            }
            finally
            {
                TryDelete(objPath);
            }
        }

        private static Md1 CreateMesh()
        {
            var builder = new Md1.Builder();
            var part = new Md1.Builder.Part();
            part.Positions.Add(new Md1.Vector(510, -487, 238));
            part.Positions.Add(new Md1.Vector(0, 1000, -2000));
            part.Positions.Add(new Md1.Vector(-1, 0, 1));
            part.Normals.Add(new Md1.Vector(100, -200, 300));
            part.Triangles.Add(new Md1.Triangle()
            {
                n0 = 0,
                v0 = 0,
                n1 = 0,
                v1 = 1,
                n2 = 0,
                v2 = 2
            });
            part.TriangleTextures.Add(new Md1.TriangleTexture()
            {
                u0 = 0,
                v0 = 0,
                u1 = 96,
                v1 = 192,
                u2 = 64,
                v2 = 64,
                page = 0
            });
            builder.Parts.Add(part);
            return builder.ToMesh();
        }

        private static void WithCommaDecimalCulture(Action action)
        {
            var original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CreateCommaDecimalCulture();
                action();
            }
            finally
            {
                CultureInfo.CurrentCulture = original;
            }
        }

        private static CultureInfo CreateCommaDecimalCulture()
        {
            try
            {
                return new CultureInfo("es-ES");
            }
            catch (CultureNotFoundException)
            {
                var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                culture.NumberFormat.NumberDecimalSeparator = ",";
                return culture;
            }
        }

        private static void TryDelete(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
