# Welcome to AGB Contributing!

Thanks for your interest in helping improve AGB, this is a helper guide for developers to help your way around.

# Workflow

In general, submit a pull request with your changes and a description of what changed. Your code should be compatible
across Linux and Windows.

## Terminology

**_Do not mention the game by name anywhere here._**
Instead, use terms like `Client` & `Game` (the thing to play the game), `Server` & `API`, or `Service Provider` (the entity).

We use terms such as Allowlist and Blocklist over "Whitelist" / "Blacklist", they are easier to
enumerate (`Blocklist -> Block -> Blocks` vs `Blacklist -> Black -> Blacks`) and more inclusive.

# Blocklist guidelines

Blocklists are a very powerful tool since they allow AGB users to directly influence their reality,
therefore, we want for core lists to be more strictly regulated.

If you are maintaining your own blocklist, I highly recommend you at least consider these guidelines.

## Specificity

If a world has a hierarchy like this:

- Cube
- Cube1
    - Cube2
        - Cube3
        - Cube
- Something else

And your blocklist is like this:

```toml
game_objects = [{ name = "Cube" }]
```

AGB will disable all GameObjects named `Cube`.
This can be desirable if an Object has a specific name repeated across the world.

However, if the name is as generic as `Cube`, you might end up disabling other objects that could be important without
intending to, such as geometry. **Don't excessively disable objects.**

The Blocklist component gives you two fields to mitigate this, you can match to the Object's Position and Parent (
Parent === Unity `m_Father`).

This matches an Object named `Cube` with that specific position.

```toml
game_objects = [
    { name = "Cube", position = { x = 26.773, y = 3.244, z = -13.982 } },
]
```

This matches an Object named `Cube` with a parent that has the name `Cube1`.

```toml
game_objects = [
    { name = "Cube", parent = { name = "Cube1" } }
]
```

You can use both of these to resolve (hopefully) a very specific object.

```toml
game_objects = [
    { name = "Cube", position = { x = 26.773, y = 3.244, z = -13.982 }, parent = { name = "Cube1" } },
]
```

Note that we don't resolve a nested parent, so if you do something like this:

```toml
# Cube3 has no effect here because it's nested in the parent.
game_objects = [
    { name = "Cube3", parent = { name = "Cube2", parent = { name = "Cube3" } } }
]
```

## Avoid touching logic-related objects

<sub>Or don't break stuff</sub>

Certain objects exist in worlds to control logic, for example, controlling and regulating players in game worlds.

***DO NOT, UNDER ANY CIRCUMSTANCE, DISABLE GAME LOGIC.
IF YOUR BLOCKLIST CAUSES FUNDAMENTAL WORLD BREAKAGE, NO BLOCK IS PREFERRED.***

This is informed by the arguments against modding before the EAC days. Creators were complaining that mods would
disrupt worlds and require them to diagnose those issues.

## Use only one table format

Due to a [possible bug(?)](https://github.com/xoofx/Tomlyn/issues/74) in Tomlyn, you cannot mix the inline and
non-inline format:

```toml
# This works
title = "Example"
description = ""
maintainer = ""
[[block]]
friendly_name = "World"
world_id = "wrld_00000000-0000-0000-0000-000000000000"
game_objects = [{ name = "1" }]

[[block]]
friendly_name = "World"
world_id = "wrld_00000000-0000-0000-0000-000000000001"
game_objects = [{ name = "2" }]
```

```toml
# This works as well
title = "Example"
description = ""
maintainer = ""
[[block]]
friendly_name = "World"
world_id = "wrld_00000000-0000-0000-0000-000000000000"
[[block.game_objects]]
name = "1"

[[block]]
friendly_name = "World"
world_id = "wrld_00000000-0000-0000-0000-000000000000"
[[block.game_objects]]
name = "2"
```

```toml
# This will error due to being counted as a re-assign.
title = "Example"
description = ""
maintainer = ""
[[block]]
friendly_name = "World"
world_id = "wrld_00000000-0000-0000-0000-000000000000"
[[block.game_objects]]
name = "1"

[[block]]
friendly_name = "World"
world_id = "wrld_00000000-0000-0000-0000-000000000000"
game_objects = { name = "1" }
```

In AGB core blocklists, we use an [inline table](https://toml.io/en/v1.0.0#inline-table) with linebreaks intentionally
*against the recommendations of the TOML specification.*
If you maintain your own list, you are free to choose whatever suits you as long as you do not mix them.

# Plugins

AdGoBye.ExamplePlugin contains an example using AssetsTools.NET that disables chairs, you can use whatever you'd like to
parse the files.
[The Plugin interface](AdGoBye.Plugins/IPlugin.cs) contains specifics for implementation.

Since you are given arbitrary code execution, it'd be preferable that your code is public and permissively licensed.
This allows others to verify and adapt your code.

If your actions are destructive, it might make sense to leave a backup file of the world before the plugin's changes.

If your Plugin does changes that are so substantial or might conflict with blocklists, you can use `OverrideBlocklist`.


# Tangents

## Couldn't this code disable avatar things as well?

On a technical level? Of course.

The code is designed for matching worlds, and we explicitly check if something is a World.
You can disable this check, but it's not as useful as you think. We cannot edit something before it is being loaded, or
while it's in transit. If you attempt to edit something *while* it's being loaded, you will crash the game.

This means if you try to use blocklists for something like anti-crashing, you will need to load the offending avatar
at least once, so it can get saved to disk before AGB can block it.
That limitation is fine for worlds, but this won't work if loading the avatar might crash your game.
Even then, we match using IDs, so if the avatar gets ripped or re-uploaded, the block won't work.

But please don't do this either way:
Often when you see someone, you don't say `That's Regalia's Avatar`, you say `That's Regalia` (
see [Identity, Gender, and VRChat](https://youtu.be/5v_Dl7i4Bcw)).
Avatars are personal to people because you inhabit and exist within an avatar, they are direct representations of
people.
If you edit someone else's Avatar like this, you are directly choosing how that person should be able to express
themselves.
That is misguided at best and dangerously controlling at worst.

The game has way better tools to prevent disruptive avatars or users (by using restrictive settings, hiding
avatars, blocking someone, group moderators, or calling a vote kick).

Avatars are an incredibly sensitive and intimate topic for people since they're a form of free self-expression.

I hope this is a convincing enough argument for you to leave avatars be.
