using System.Collections.Generic;
using UnityEngine;

// A Dynamic, Loose Octree for storing any objects that can be described with AABB bounds
// See also: PointOctree, where objects are stored as single points and some code can be simplified
// Octree:	An octree is a tree data structure which divides 3D space into smaller partitions (nodes)
//			and places objects into the appropriate nodes. This allows fast access to objects
//			in an area of interest without having to check every object.
// Dynamic: The octree grows or shrinks as required when objects as added or removed
//			It also splits and merges nodes as appropriate. There is no maximum depth.
//			Nodes have a constant - numObjectsAllowed - which sets the amount of items allowed in a node before it splits.
// Loose:	The octree's nodes can be larger than 1/2 their parent's length and width, so they overlap to some extent.
//			This can alleviate the problem of even tiny objects ending up in large nodes if they're near boundaries.
//			A looseness value of 1.0 will make it a "normal" octree.
// T:		The content of the octree can be anything, since the bounds data is supplied separately.

// Originally written for my game Scraps (http://www.scrapsgame.com) but intended to be general-purpose.
// Copyright 2014 Nition, BSD licence (see LICENCE file). http://nition.co
// Unity-based, but could be adapted to work in pure C#

// Note: For loops are often used here since in some cases (e.g. the IsColliding method)
// they actually give much better performance than using Foreach, even in the compiled build.
// Using a LINQ expression is worse again than Foreach.
namespace UnityOctree.Scripts {
  /// <summary>
  /// 
  /// </summary>
  /// <typeparam name="T"></typeparam>
  public class BoundsOctree<T> {
    // The total amount of objects currently in the tree
    public int Count { get; private set; }

    // Root node of the octree
    BoundsOctreeNode<T> _root_node;

    // Should be a value between 1 and 2. A multiplier for the base size of a node.
    // 1.0 is a "normal" octree, while values > 1 have overlap
    readonly float _looseness;

    // Size that the octree was on creation
    readonly float _initial_size;

    // Minimum side length that a node can be - essentially an alternative to having a max depth
    readonly float _min_size;
    // For collision visualisation. Automatically removed in builds.
    #if UNITY_EDITOR
    const int _num_collisions_to_save = 4;
    readonly Queue<Bounds> _last_bounds_collision_checks = new Queue<Bounds>();
    readonly Queue<Ray> _last_ray_collision_checks = new Queue<Ray>();
    #endif

    /// <summary>
    /// Constructor for the bounds octree.
    /// </summary>
    /// <param name="initial_world_size">Size of the sides of the initial node, in metres. The octree will never shrink smaller than this.</param>
    /// <param name="initial_world_pos">Position of the centre of the initial node.</param>
    /// <param name="min_node_size">Nodes will stop splitting if the new nodes would be smaller than this (metres).</param>
    /// <param name="looseness_val">Clamped between 1 and 2. Values > 1 let nodes overlap.</param>
    public BoundsOctree(float initial_world_size,
                        Vector3 initial_world_pos,
                        float min_node_size,
                        float looseness_val) {
      if (min_node_size > initial_world_size) {
        Debug.LogWarning("Minimum node size must be at least as big as the initial world size. Was: "
                         + min_node_size
                         + " Adjusted to: "
                         + initial_world_size);
        min_node_size = initial_world_size;
      }

      this.Count = 0;
      this._initial_size = initial_world_size;
      this._min_size = min_node_size;
      this._looseness = Mathf.Clamp(looseness_val, 1.0f, 2.0f);
      this._root_node =
          new BoundsOctreeNode<T>(this._initial_size, this._min_size, this._looseness, initial_world_pos);
    }

    // #### PUBLIC METHODS ####

    /// <summary>
    /// Add an object.
    /// </summary>
    /// <param name="obj">Object to add.</param>
    /// <param name="obj_bounds">3D bounding box around the object.</param>
    public void Add(T obj, Bounds obj_bounds) {
      // Add object or expand the octree until it can be added
      var count = 0; // Safety check against infinite/excessive growth
      while (!this._root_node.Add(obj, obj_bounds)) {
        this.Grow(obj_bounds.center - this._root_node.Center);
        if (++count > 20) {
          Debug.LogError("Aborted Add operation as it seemed to be going on forever ("
                         + (count - 1)
                         + ") attempts at growing the octree.");
          return;
        }
      }

      this.Count++;
    }

    /// <summary>
    /// Remove an object. Makes the assumption that the object only exists once in the tree.
    /// </summary>
    /// <param name="obj">Object to remove.</param>
    /// <returns>True if the object was removed successfully.</returns>
    public bool Remove(T obj) {
      var removed = this._root_node.Remove(obj);

      // See if we can shrink the octree down now that we've removed the item
      if (removed) {
        this.Count--;
        this.Shrink();
      }

      return removed;
    }

    /// <summary>
    /// Removes the specified object at the given position. Makes the assumption that the object only exists once in the tree.
    /// </summary>
    /// <param name="obj">Object to remove.</param>
    /// <param name="obj_bounds">3D bounding box around the object.</param>
    /// <returns>True if the object was removed successfully.</returns>
    public bool Remove(T obj, Bounds obj_bounds) {
      var removed = this._root_node.Remove(obj, obj_bounds);

      // See if we can shrink the octree down now that we've removed the item
      if (removed) {
        this.Count--;
        this.Shrink();
      }

      return removed;
    }

    /// <summary>
    /// Check if the specified bounds intersect with anything in the tree. See also: GetColliding.
    /// </summary>
    /// <param name="check_bounds">bounds to check.</param>
    /// <returns>True if there was a collision.</returns>
    public bool IsColliding(Bounds check_bounds) {
      //#if UNITY_EDITOR
      // For debugging
      //AddCollisionCheck(checkBounds);
      //#endif
      return this._root_node.IsColliding(ref check_bounds);
    }

    /// <summary>
    /// Check if the specified ray intersects with anything in the tree. See also: GetColliding.
    /// </summary>
    /// <param name="check_ray">ray to check.</param>
    /// <param name="max_distance">distance to check.</param>
    /// <returns>True if there was a collision.</returns>
    public bool IsColliding(Ray check_ray, float max_distance) {
      //#if UNITY_EDITOR
      // For debugging
      //AddCollisionCheck(checkRay);
      //#endif
      return this._root_node.IsColliding(ref check_ray, max_distance);
    }

    /// <summary>
    /// Returns an array of objects that intersect with the specified bounds, if any. Otherwise returns an empty array. See also: IsColliding.
    /// </summary>
    /// <param name="colliding_with">list to store intersections.</param>
    /// <param name="check_bounds">bounds to check.</param>
    /// <returns>Objects that intersect with the specified bounds.</returns>
    public void GetColliding(List<T> colliding_with, Bounds check_bounds) {
      //#if UNITY_EDITOR
      // For debugging
      //AddCollisionCheck(checkBounds);
      //#endif
      this._root_node.GetColliding(ref check_bounds, colliding_with);
    }

    /// <summary>
    /// Returns an array of objects that intersect with the specified ray, if any. Otherwise returns an empty array. See also: IsColliding.
    /// </summary>
    /// <param name="colliding_with">list to store intersections.</param>
    /// <param name="check_ray">ray to check.</param>
    /// <param name="max_distance">distance to check.</param>
    /// <returns>Objects that intersect with the specified ray.</returns>
    public void GetColliding(List<T> colliding_with,
                             Ray check_ray,
                             float max_distance = float.PositiveInfinity) {
      //#if UNITY_EDITOR
      // For debugging
      //AddCollisionCheck(checkRay);
      //#endif
      this._root_node.GetColliding(ref check_ray, colliding_with, max_distance);
    }

    public List<T> GetWithinFrustum(Camera cam) {
      var planes = GeometryUtility.CalculateFrustumPlanes(cam);

      var list = new List<T>();
      this._root_node.GetWithinFrustum(planes, list);
      return list;
    }

    public Bounds GetMaxBounds() { return this._root_node.GetBounds(); }

    /// <summary>
    /// Draws node boundaries visually for debugging.
    /// Must be called from OnDrawGizmos externally. See also: DrawAllObjects.
    /// </summary>
    public void DrawAllBounds() { this._root_node.DrawAllBounds(); }

    /// <summary>
    /// Draws the bounds of all objects in the tree visually for debugging.
    /// Must be called from OnDrawGizmos externally. See also: DrawAllBounds.
    /// </summary>
    public void DrawAllObjects() { this._root_node.DrawAllObjects(); }

    // Intended for debugging. Must be called from OnDrawGizmos externally
    // See also DrawAllBounds and DrawAllObjects
    /// <summary>
    /// Visualises collision checks from IsColliding and GetColliding.
    /// Collision visualisation code is automatically removed from builds so that collision checks aren't slowed down.
    /// </summary>
    #if UNITY_EDITOR
    public void DrawCollisionChecks() {
      var count = 0;
      foreach (var collision_check in this._last_bounds_collision_checks) {
        Gizmos.color = new Color(1.0f, 1.0f - ((float)count / _num_collisions_to_save), 1.0f);
        Gizmos.DrawCube(collision_check.center, collision_check.size);
        count++;
      }

      foreach (var collision_check in this._last_ray_collision_checks) {
        Gizmos.color = new Color(1.0f, 1.0f - ((float)count / _num_collisions_to_save), 1.0f);
        Gizmos.DrawRay(collision_check.origin, collision_check.direction);
        count++;
      }

      Gizmos.color = Color.white;
    }
    #endif

    // #### PRIVATE METHODS ####

    /// <summary>
    /// Used for visualising collision checks with DrawCollisionChecks.
    /// Automatically removed from builds so that collision checks aren't slowed down.
    /// </summary>
    /// <param name="check_bounds">bounds that were passed in to check for collisions.</param>
    #if UNITY_EDITOR
    void AddCollisionCheck(Bounds check_bounds) {
      this._last_bounds_collision_checks.Enqueue(check_bounds);
      if (this._last_bounds_collision_checks.Count > _num_collisions_to_save) {
        this._last_bounds_collision_checks.Dequeue();
      }
    }
    #endif

    /// <summary>
    /// Used for visualising collision checks with DrawCollisionChecks.
    /// Automatically removed from builds so that collision checks aren't slowed down.
    /// </summary>
    /// <param name="check_ray">ray that was passed in to check for collisions.</param>
    #if UNITY_EDITOR
    void AddCollisionCheck(Ray check_ray) {
      this._last_ray_collision_checks.Enqueue(check_ray);
      if (this._last_ray_collision_checks.Count > _num_collisions_to_save) {
        this._last_ray_collision_checks.Dequeue();
      }
    }
    #endif

    /// <summary>
    /// Grow the octree to fit in all objects.
    /// </summary>
    /// <param name="direction">Direction to grow.</param>
    void Grow(Vector3 direction) {
      var x_direction = direction.x >= 0 ? 1 : -1;
      var y_direction = direction.y >= 0 ? 1 : -1;
      var z_direction = direction.z >= 0 ? 1 : -1;
      var old_root = this._root_node;
      var half = this._root_node.BaseLength / 2;
      var new_length = this._root_node.BaseLength * 2;
      var new_center = this._root_node.Center
                       + new Vector3(x_direction * half, y_direction * half, z_direction * half);

      // Create a new, bigger octree root node
      this._root_node = new BoundsOctreeNode<T>(new_length, this._min_size, this._looseness, new_center);

      if (old_root.HasAnyObjects()) {
        // Create 7 new octree children to go with the old root as children of the new root
        var root_pos = this._root_node.BestFitChild(old_root.Center);
        var children = new BoundsOctreeNode<T>[8];
        for (var i = 0; i < 8; i++) {
          if (i == root_pos) {
            children[i] = old_root;
          } else {
            x_direction = i % 2 == 0 ? -1 : 1;
            y_direction = i > 3 ? -1 : 1;
            z_direction = (i < 2 || (i > 3 && i < 6)) ? -1 : 1;
            children[i] = new BoundsOctreeNode<T>(old_root.BaseLength,
                                                  this._min_size,
                                                  this._looseness,
                                                  new_center
                                                  + new Vector3(x_direction * half,
                                                                y_direction * half,
                                                                z_direction * half));
          }
        }

        // Attach the new children to the new root node
        this._root_node.SetChildren(children);
      }
    }

    /// <summary>
    /// Shrink the octree if possible, else leave it the same.
    /// </summary>
    void Shrink() { this._root_node = this._root_node.ShrinkIfPossible(this._initial_size); }
  }
}
