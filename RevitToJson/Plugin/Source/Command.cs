using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.Attributes;

namespace CoT
{
  [Transaction( TransactionMode.Manual )]
  [Regeneration( RegenerationOption.Manual )]
  [Journaling( JournalingMode.NoCommandData )]
  public class RevitToJson : IExternalCommand
  {
    #region -- Local State Variables ------------------------------------------

    /// <summary>
    /// UI Application Reference
    /// </summary>
    private UIApplication UIApplication;

    /// <summary>
    /// UI Document Reference
    /// </summary>
    private UIDocument UIDocument;

    /// <summary>
    /// Active Document Reference
    /// </summary>
    private Document Document;

    /// <summary>
    /// Json Stream
    /// </summary>
    private StringBuilder Json;

    /// <summary>
    /// Logs
    /// </summary>
    private StringBuilder Logs;

    /// <summary>
    /// Current Element
    /// </summary>
    private Element Element;

    #endregion

    #region -- Write Document -------------------------------------------------

    /// <summary>
    /// Command Entry Point
    /// </summary>
    /// <param name="data">Command data is ignored</param>
    /// <param name="message">Result message is ignored</param>
    /// <param name="elementset">Element set is ignored</param>
    /// <returns>Success</returns>
    public Result Execute( ExternalCommandData data, ref string message, ElementSet elementset )
    {
      //-- Store Local State
      //--
      UIApplication = data.Application;
      UIDocument = UIApplication.ActiveUIDocument;
      Document = UIDocument.Document;
      {
        var dialog = new SaveFileDialog
        {
          Filter = "Revit Json Files (*.json)|*.json|All Files (*.*)|*.*",
          FileName = string.IsNullOrEmpty( Document.PathName ) ? "Untitled.json" : 
            $"{Path.GetFileNameWithoutExtension( Document.PathName )}.json"
        };
        if( dialog.ShowDialog( ) == DialogResult.OK )
        {
          Json = new StringBuilder( );
          Logs = new StringBuilder( );
          {
            var tick = Environment.TickCount;
            var result = DocumentToJson( );
            Logs.AppendLine( $"Elapsed {( Environment.TickCount - tick ) / 1000.0:0.0}sec" );
            Logs.AppendLine( result ? "Success" : "Failure" );            
            File.WriteAllText( dialog.FileName, Json.ToString( ) );
          }          
          MessageBox.Show( Logs.ToString( ), "Revit To Json", 
            MessageBoxButtons.OK, MessageBoxIcon.Information );
          Json = null;
          Logs = null;
        }
      }
      UIApplication = null;
      UIDocument = null;
      Document = null;

      return Result.Succeeded;
    }

    /// <summary>
    /// Document Elements Iterator
    /// </summary>
    public IEnumerable<Element> Elements
    {
      get
      {
        var dummy = new FilteredElementCollector( Document )
          .OfClass( typeof( Family ) ).FirstElement( );
        yield return dummy;

        var elements = new FilteredElementCollector( Document )
          .WherePasses( new ExclusionFilter( new List<ElementId>( ) { dummy.Id } ) );
        foreach( var element in elements )
        {
          yield return element;
        }
      }
    }

    /// <summary>
    /// Document to Json
    /// </summary>
    /// <returns>Pass/Fail</returns>
    private bool DocumentToJson( )
    {
      var elements = new List<Element>( );
      foreach( var element in Elements )
      {
        if( element is FamilySymbol ) continue;
        if( element is ElementType  ) continue;
        elements.Add( element );
      }

      var result = false;
      Json.AppendLine( "{" );
      for( var index = 0; index < elements.Count; index++ )
      {
        Element = elements[index];
        result |= ElementToJson( Element, 
          index == elements.Count - 1 );
      }
      Element = null;
      Json.Append( "}" );
      return result;
    }

    /// <summary>
    /// Write Element to Json Stream
    /// </summary>
    /// <param name="element">Element</param>
    /// <param name="last">Is Last Element</param>
    /// <returns>Pass/Fail</returns>
    /// <remarks>
    /// Revit elements are formated as 
    /// "GUID" : { 
    ///   "Geometry": [], 
    ///   "Materials": [], 
    ///   "Attributes": {} 
    /// }
    /// </remarks>
    private bool ElementToJson( Element element, bool last )
    {
      var result = false;
      Json.AppendLine( $"\"{element.UniqueId}\": {{" );
      var geometries = element.get_Geometry( new Options
      {
        ComputeReferences = true,
        DetailLevel = ViewDetailLevel.Fine,
      });
      if( geometries != null )
      {
        result |= GeometryToJson( geometries, Transform.Identity );
        result |= MaterialToJson( element, geometries );
      }
      else
      {
        Json.AppendLine( "\t\"Geometry\": []," );
        Json.AppendLine( "\t\"Materials\": []," );
      }
      result |= AttributesToJson( element );
      Json.AppendLine( $"}}{( last ? "" : "," )}" );
      return result;
    }

    #endregion

    #region -- Write Geometry -------------------------------------------------

    /// <summary>
    /// Write Geometry to Json Stream
    /// </summary>
    /// <param name="geometries">Geometry Element</param>
    /// <param name="transform">Transformation</param>
    /// <param name="recusive">Recursive Mode</param>
    /// <returns>Pass/Fail</returns>
    private bool GeometryToJson( GeometryElement geometries, 
      Transform transform, bool recusive = false )
    {
      var result = false;
      if( !recusive )
        Json.AppendLine( "\t\"Geometry\":\t[" );
      foreach( var geometry in geometries )
      {
        if( geometry is Solid )
        {
          result |= SolidToJson( geometry as Solid, transform );
        }
        else if( geometry is Mesh )
        {
          result |= MeshToJson( geometry as Mesh, transform );
        }
        else if( geometry is Point )
        {
          result |= PointToJson( geometry as Point, transform );
        }
        else if( geometry is Line )
        {
          result |= LineToJson( geometry as Line, transform );
        }
        else if( geometry is PolyLine )
        {
          result |= PolylineToJson( geometry as PolyLine, transform );
        }
        else if( geometry is Curve )
        {
          result |= CurveToJson( geometry as Curve, transform );
        }
        else if( geometry is Profile )
        {
          result |= ProfileToJson( geometry as Profile, transform );
        }
        else if( geometry is Face )
        {
          result |= FaceToJson( geometry as Face, transform );
        }
        else if( geometry is GeometryInstance )
        {
          var instance = geometry as GeometryInstance;
          result |= GeometryToJson( instance.SymbolGeometry, instance.Transform, true );
        }
      }
      if( !recusive )
      {
        Json.AppendLine( $"\t\t{{ \"Type\": \"\" }}" );
        Json.AppendLine( "\t]," );
      }
      return result;
    }

    /// <summary>
    /// Write Point Geometry to Json Stream
    /// </summary>
    /// <param name="geometry">Point geometry</param>
    /// <param name="transform">Transformation matrix</param>
    /// <param name="source">Source element type</param>
    /// <returns>Pass/Fail</returns>
    private bool PointToJson( Point geometry, 
      Transform transform, string source = "Point" )
    {
      Json.AppendLine( $"\t\t{{" );
      Json.AppendLine( $"\t\t\t\"Type\": \"Node\"," );
      Json.AppendLine( $"\t\t\t\"Kind\": \"{source}\"," );
      Json.AppendLine( $"\t\t\t\"Coords\": {XyzToJson( transform.OfPoint( geometry.Coord ) )}" );
      Json.AppendLine( $"\t\t}}," );
      return true;
    }

    /// <summary>
    /// Write Sequence of Points to Json Stream
    /// </summary>
    /// <param name="geometry">Sequence of Points</param>
    /// <param name="transform">Transformation matrix</param>
    /// <param name="source">Source element type</param>
    /// <returns>Pass/Fail</returns>
    private bool PointsToJson( IList<XYZ> geometry, 
      Transform transform, string source )
    {
      if( geometry.Count < 2 )
      {
        Logs.AppendLine( $"Failed Points to Json at Element Id {Element.Id} Name \"{Element.Name}\"" );
        return false;
      }

      Json.AppendLine( "\t\t{" );
      Json.AppendLine( "\t\t\t\"Type\":\t\"Poly\"," );
      Json.AppendLine( $"\t\t\t\"Kind\":\t\"{source}\"," );
      Json.AppendLine( $"\t\t\t\"Points\":\t[" );

      for( var index = 0; index < geometry.Count - 1; index++ )
      {
        var point = transform.OfPoint(geometry[index]);
        Json.AppendLine( $"\t\t\t\t{XyzToJson( point )}," );
      }
      {
        var point = transform.OfPoint(geometry[geometry.Count - 1]);
        Json.AppendLine( $"\t\t\t\t{XyzToJson( point )}" );
      }

      Json.AppendLine( "\t\t\t]" );
      Json.AppendLine( $"\t\t}}," );
      return true;
    }

    /// <summary>
    /// Write Line Segment to Json Stream
    /// </summary>
    /// <param name="geometry">Line Geometry</param>
    /// <param name="transform">Transformation matrix</param>
    /// <param name="source">Source element type</param>
    /// <returns>Pass/Fail</returns>
    private bool LineToJson( Line geometry, 
      Transform transform, string source = "Line" ) =>
      PointsToJson( geometry.Tessellate( ), transform, source );

    /// <summary>
    /// Write Polyline to Json Stream
    /// </summary>
    /// <param name="geometry">Polyline Geometry</param>
    /// <param name="transform">Transformation matrix</param>
    /// <param name="source">Source element type</param>
    /// <returns>Pass/Fail</returns>
    private bool PolylineToJson( PolyLine geometry, 
      Transform transform, string source = "PolyLine" ) =>
      PointsToJson( geometry.GetCoordinates( ), transform, source );

    /// <summary>
    /// Write Edge to Json Stream
    /// </summary>
    /// <param name="geometry">Edge Geometry</param>
    /// <param name="transform">Transformation matrix</param>
    /// <param name="source">Source element type</param>
    /// <returns>Pass/Fail</returns>
    private bool EdgeToJson( Edge geometry, 
      Transform transform, string source = "Edge" ) =>
      PointsToJson( geometry.Tessellate( ), transform, source );

    /// <summary>
    /// Write Curve to Json Stream
    /// </summary>
    /// <param name="geometry">Curve Geometry</param>
    /// <param name="transform">Transformation matrix</param>
    /// <param name="source">Source element type</param>
    /// <returns>Pass/Fail</returns>
    private bool CurveToJson( Curve geometry, 
      Transform transform, string source = "Curve" ) =>
      PointsToJson( geometry.Tessellate( ), transform, source );

    /// <summary>
    /// Write Profile to Json Stream
    /// </summary>
    /// <param name="geometry">Profile Geometry</param>
    /// <param name="transform">Transformation matrix</param>
    /// <param name="source">Source element type</param>
    /// <returns>Pass/Fail</returns>
    private bool ProfileToJson( Profile geometry, 
      Transform transform, string source = "Profile" )
    {
      var result = false;
      foreach( Curve curve in geometry.Curves )
      {
        result |= CurveToJson( curve, transform, source );
      }
      return result;
    }

    /// <summary>
    /// Write Mesh to Json Stream
    /// </summary>
    /// <param name="geometry">Profile Geometry</param>
    /// <param name="transform">Transformation matrix</param>
    /// <param name="source">Source element type</param>
    /// <returns>Pass/Fail</returns>
    private bool MeshToJson( Mesh geometry, 
      Transform transform, string source = "Mesh" )
    {
      var planes = new List<Tuple<uint, uint, uint>>();
      var count = geometry.NumTriangles;
      for( var index = 0; index < count; index++ )
      {
        var plane = geometry.get_Triangle(index);
        planes.Add( new Tuple<uint, uint, uint>(
          plane.get_Index( 0 ),
          plane.get_Index( 1 ),
          plane.get_Index( 2 ) ) );
      }
      return MeshToJson( geometry.Vertices, planes, transform, source );
    }

    /// <summary>
    /// Write Mesh to Json Stream
    /// </summary>
    /// <param name="points">Point list</param>
    /// <param name="planes">Plane list</param>
    /// <param name="transform">Transformation matrix</param>
    /// <param name="json">Json text stream</param>
    /// <param name="source">Source element type</param>
    /// <returns>Pass/Fail</returns>
    private bool MeshToJson( IList<XYZ> points, IList<Tuple<uint, uint, uint>> planes, 
       Transform transform, string source )
    {
      Json.AppendLine( "\t\t{" );
      Json.AppendLine( "\t\t\t\"Type\":\t\"Mesh\"," );
      Json.AppendLine( $"\t\t\t\"Kind\":\t\"{source}\"," );
      {
        Json.AppendLine( $"\t\t\t\"Points\":\t[" );
        for( var index = 0; index < points.Count - 1; index++ )
        {
          var point = transform.OfPoint(points[index]);
          Json.AppendLine( $"\t\t\t\t{XyzToJson( point )}," );
        }
        {
          var point = transform.OfPoint(points[points.Count - 1]);
          Json.AppendLine( $"\t\t\t\t{XyzToJson( point )}" );
        }
        Json.AppendLine( "\t\t\t]," );
      }
      {
        Json.AppendLine( $"\t\t\t\"Planes\":\t[" );
        for( var index = 0; index < planes.Count - 1; index++ )
        {
          var plane = planes[index];
          Json.AppendLine( $"\t\t\t\t{FormatPlane( plane )}," );
        }
        {
          var plane = planes[planes.Count - 1];
          Json.AppendLine( $"\t\t\t\t{FormatPlane( plane )}" );
        }
        Json.AppendLine( "\t\t\t]" );
      }
      Json.AppendLine( $"\t\t}}," );
      return true;
    }

    /// <summary>
    /// Write Mesh to Json Stream
    /// </summary>
    /// <param name="geometry">Solid Geometry</param>
    /// <param name="transform">Transformation matrix</param>
    /// <returns>Pass/Fail</returns>
    private bool SolidToJson( Solid geometry, Transform transform )
    {
      try
      {
        if( geometry.Faces.Size == 0 )
        {
          if( geometry.Edges.Size == 0 )
          {
            return false;
          }
          var result = false;
          var edges = geometry.Edges.Size;
          for( var index = 0; index < edges; index++ )
            result |= EdgeToJson( geometry.Edges.get_Item( index ), transform );
          return result;
        }

        var mesh = SolidUtils.TessellateSolidOrShell(
          geometry, new SolidOrShellTessellationControls());

        var count = mesh.ShellComponentCount;
        if( count == 0 ) return false;

        for( var index = 0; index < count; index++ )
        {
          var component = mesh.GetShellComponent(index);

          var points = new List<XYZ>();
          var planes = new List<Tuple<uint, uint, uint>>();

          var point_count = component.VertexCount;
          for( var point_index = 0; point_index < point_count; point_index++ )
            points.Add( component.GetVertex( point_index ) );

          var plane_count = component.TriangleCount;
          for( var plane_index = 0; plane_index < plane_count; plane_index++ )
          {
            var plane = component.GetTriangle(plane_index);
            planes.Add( new Tuple<uint, uint, uint>(
              (uint) plane.VertexIndex0,
              (uint) plane.VertexIndex1,
              (uint) plane.VertexIndex2 ) );
          }

          MeshToJson( points, planes, transform, "Solid" );
        }
        return true;
      }
      catch
      {
        Logs.AppendLine( $"Failed Solid to Json at Element Id {Element.Id} Name \"{Element.Name}\"" );

        var result = false;
        var count = geometry.Faces.Size;
        for( var index = 0; index < count; index++ )
        {
          result |= MeshToJson( geometry.Faces.get_Item( index )
            .Triangulate( ), transform, "Face" );
        }
        return result;
      }
    }

    /// <summary>
    /// Write Face to Json Stream
    /// </summary>
    /// <param name="geometry">Face Geometry</param>
    /// <param name="transform">Transformation matrix</param>
    /// <param name="json">Json text stream</param>
    /// <param name="source">Source element type</param>
    /// <returns>Pass/Fail</returns>
    private bool FaceToJson( Face geometry, 
      Transform transform, string source = "Face" )
    {
      try
      {
        return MeshToJson( geometry.Triangulate( ), transform, source );
      }
      catch
      {
        Logs.AppendLine( $"Failed Face to Json at Element Id {Element.Id} Name \"{Element.Name}\"" );
        return false;
      }
    }

    #endregion

    #region -- Write Attributes -----------------------------------------------

    private bool AttributesToJson( Element element )
    {
      var attributes = new Dictionary<string, int>();

      Json.AppendLine( $"\t\"Attributes\": {{" );

      Json.AppendLine( $"\t\t\t\"Name\": \"{StringToJson( element.Name )}\"," );
      attributes.Add( "Name", 0 );

      Json.AppendLine( $"\t\t\t\"Guid\": \"{element.Id.IntegerValue}\"," );
      attributes.Add( "Guid", 0 );

      Json.AppendLine( $"\t\t\t\"Type\": \"{element.GetType( ).Name}\"," );
      attributes.Add( "Type", 0 );

      if( element is FamilyInstance )
      {
        Json.AppendLine( $"\t\t\t\"Symbol\": \"{StringToJson( ( element as FamilyInstance ).Symbol.Name )}\"," );
        attributes.Add( "Symbol", 0 );
      }

      var category = element.Category;
      if( category != null )
      {
        Json.AppendLine( $"\t\t\t\"Category\": {{" );
        Json.AppendLine( $"\t\t\t\t\"Name\": \"{StringToJson( category.Name )}\"," );
        Json.AppendLine( $"\t\t\t\t\"Guid\": {category.Id.IntegerValue}," );
        Json.AppendLine( $"\t\t\t\t\"Type\": \"{(BuiltInCategory) category.Id.IntegerValue}\"," );
        Json.AppendLine( $"\t\t\t\t\"Kind\": \"{( category.Parent == null ? "" : StringToJson( category.Parent.Name ) )}\"," );

        var subs = category.SubCategories;
        if( subs != null && subs.Size > 0 )
        {
          Json.AppendLine( $"\t\t\t\t\"Subs\": [" );
          var count = subs.Size;
          foreach( Category sub in subs )
          {
            Json.AppendLine( "\t\t\t\t\t{" );
            Json.AppendLine( $"\t\t\t\t\t\t\"Name\": \"{StringToJson( sub.Name )}\"," );
            Json.AppendLine( $"\t\t\t\t\t\t\"Guid\": {sub.Id.IntegerValue}," );
            Json.AppendLine( $"\t\t\t\t\t\t\"Type\": \"{(BuiltInCategory) sub.Id.IntegerValue}\"," );
            Json.AppendLine( $"\t\t\t\t\t\t\"Kind\": \"{( sub.Parent == null ? "" : StringToJson( sub.Parent.Name ) )}\"" );
            Json.AppendLine( $"\t\t\t\t\t}}{( --count == 0 ? "" : "," )}" );
          }
          Json.AppendLine( $"\t\t\t\t]" );
        }
        else
          Json.AppendLine( $"\t\t\t\t\"Subs\": []" );
        Json.AppendLine( $"\t\t\t}}," );
        attributes.Add( "Category", 0 );
      }

      foreach( Parameter param in element.Parameters )
      {
        if( param.Definition == null ) continue;
        var name = StringToJson(param.Definition.Name);
        if( attributes.ContainsKey( name ) ) continue;
        attributes.Add( name, 0 );

        switch( param.StorageType )
        {
          case StorageType.String:
          {
            Json.AppendLine( $"\t\t\t\"{name}\": \"{StringToJson( param.AsString( ) )}\"," );
          }
          break;

          case StorageType.Integer:
          {
            if( param.Definition.ParameterType == ParameterType.YesNo )
              Json.AppendLine( $"\t\t\t\"{name}\": {( param.AsInteger( ) == 0 ? "true" : "false" )}," );
            else
              Json.AppendLine( $"\t\t\t\"{name}\": {param.AsInteger( )}," );
          }
          break;

          case StorageType.Double:
          {
            Json.AppendLine( $"\t\t\t\"{name}\": {param.AsDouble( )}," );
          }
          break;

          case StorageType.ElementId:
          {
            Json.AppendLine( $"\t\t\t\"{name}\": {param.AsElementId( ).IntegerValue}," );
          }
          break;

          default:
          {
            Json.AppendLine( $"\t\t\t\"{name}\": null," );
          }
          break;
        }
      }
      Json.AppendLine( $"\t\t\t\"Pinned\": {( element.Pinned ? "true" : "false" )}" );
      Json.AppendLine( "\t}" );
      return true;
    }

    /// <summary>
    /// Format Point Coordinates to Json
    /// </summary>
    /// <param name="point">Point coordinates</param>
    /// <param name="format">Floating point to string format</param>
    /// <param name="padding">Text padding</param>
    /// <returns>"[x, y, z]"</returns>
    private string XyzToJson( XYZ point, string format = "f12", int padding = 18 )
    {
      var x = point.X.ToString(format).PadLeft(padding);
      var y = point.Y.ToString(format).PadLeft(padding);
      var z = point.Z.ToString(format).PadLeft(padding);
      return $"[{x}, {y}, {z} ]";
    }

    /// <summary>
    /// Format Mesh Face to Json
    /// </summary>
    /// <param name="plane">Mesh face indices</param>
    /// <param name="padding">White space padding</param>
    /// <returns></returns>
    private string FormatPlane( Tuple<uint, uint, uint> plane, int padding = 6 )
    {
      var a = plane.Item1.ToString( ).PadLeft( padding );
      var b = plane.Item2.ToString( ).PadLeft( padding );
      var c = plane.Item3.ToString( ).PadLeft( padding );
      return $"[{a}, {b}, {c} ]";
    }

    #endregion

    #region -- Write Materials ------------------------------------------------

    private bool MaterialToJson( Element element, GeometryElement geometries )
    {
      var materials = new Dictionary<ElementId, string>();

      var ids = element.GetMaterialIds(true);
      if( ids != null )
        foreach( var id in ids )
          if( !materials.ContainsKey( id ) )
            materials.Add( id, "Element" );

      if( geometries.MaterialElement != null )
        if( !materials.ContainsKey( geometries.MaterialElement.Id ) )
          materials.Add( geometries.MaterialElement.Id, "Geometry" );

      var style = Document.GetElement(geometries.GraphicsStyleId) as GraphicsStyle;
      if( style != null && style.GraphicsStyleCategory != null && style.GraphicsStyleCategory.Material != null )
        if( !materials.ContainsKey( style.GraphicsStyleCategory.Material.Id ) )
          materials.Add( style.GraphicsStyleCategory.Material.Id, "Style" );

      if( element.Category != null && element.Category.Material != null )
        if( !materials.ContainsKey( element.Category.Material.Id ) )
          materials.Add( element.Category.Material.Id, "Category" );

      var count = materials.Count;
      if( count == 0 )
      {
        Json.AppendLine( "\t\"Materials\": []," );
        return false;
      }

      Json.AppendLine( "\t\"Materials\": [" );
      foreach( var entry in materials )
      {
        var material = Document.GetElement(entry.Key) as Material;
        Json.AppendLine( $"\t{{" );
        Json.AppendLine( $"\t\t\"Name\": \"{StringToJson( material.Name )}\"," );
        Json.AppendLine( $"\t\t\"Guid\": {material.Id.IntegerValue}," );
        Json.AppendLine( $"\t\t\"Kind\": \"{entry.Value}\"," );
        Json.AppendLine( $"\t\t\"Color\": {( material.Color.Red << 16 ) | ( material.Color.Green << 8 ) | material.Color.Blue}," );
        Json.AppendLine( $"\t\t\"Category\": \"{material.MaterialCategory}\"," );
        Json.AppendLine( $"\t\t\"Class\": \"{material.MaterialClass}\"," );
        Json.AppendLine( $"\t\t\"Shininess\": {material.Shininess}," );
        Json.AppendLine( $"\t\t\"Smoothness\": {material.Smoothness}," );
        Json.AppendLine( $"\t\t\"Transparency\": {material.Transparency}" );
        Json.AppendLine( $"\t}}{( --count == 0 ? "" : "," )}" );
      }
      Json.AppendLine( "\t]," );
      return true;
    }

    /// <summary>
    /// Format String to Json
    /// </summary>
    /// <param name="value">Text value</param>
    /// <returns>Encoded Json string</returns>
    /// <remarks>Converts unicode characters to escape sequences</remarks>
    private string StringToJson( string value )
    {
      if( string.IsNullOrEmpty( value ) )
        return string.Empty;

      var result = new StringBuilder( );
      for( var index = 0; index < value.Length; index++ )
      {
        var c = value[index];
        switch( c )
        {
          case '"': result.Append( "\\\"" ); break;
          case '\t': result.Append( "\\t" ); break;
          case '\f': result.Append( "\\f" ); break;
          case '\r': result.Append( "\\r" ); break;
          case '\n': result.Append( "\\n" ); break;
          case '\b': result.Append( "\\b" ); break;
          case '\\': result.Append( "\\\\" ); break;
          default:
            if( c < 0x20 || c > 0x7f )
              result.Append( $"\\u{(int) c:X4}" );
            else
              result.Append( c );
            break;
        }
      }
      return result.ToString( );
    }

    #endregion
  }
}