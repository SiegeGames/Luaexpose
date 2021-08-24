# LuaExpose   

## Introduction
LuaExpose is a tool designed to generate C++ code to expose your C++ source to Lua using Sol3. Additionally TypeScript definition files are generated intended to be used for TypeScriptToLua. This is done by adding C++ attributes to your header files.

## Disclaimer

LuaExpose was developed as a tool for a specific project and consequently has some specialized code still that likely needs to be worked around until it's made to be more generalized.

## Contributing

As mentioned above there are places that can be generalized better and many features could be cleaned up or extended upon. Any PRs that do this are appreciated.

## Example

```c++
class [[LUA_USERTYPE]] Entity {
    [[LUA_CTOR]] Entity(uint32_t id);
    [[LUA_FUNC]] 
    [[LUA_VAR]] uint32_t id;
}
```

## Supported Attributes

### Exposing Usertypes

+ **LUA_USERTYPE**

    Exposes a `class` or `struct` including the default constructor and any exposed constructors, functions and variables. Takes an optional parameter that adds the usertype to a group with the specified name.

+ **LUA_USERTYPE_NO_CTOR**

    Same as LUA_USERTYPE without a default constructor.

+ **LUA_USERTYPE_ENUM**

    Exposes an `enum` or `enum class` with all of the values.

+ **LUA_USERTYPE_NAMESPACE**

    Exposes a namespace with any exposed functions and variables.

+ **LUA_USERTYPE_TEMPLATE**

    Exposes a templated `class` and takes arguments for usings to all of the uses for the template.

        using Vector = Vec2<float>;
        using IntVector = Vec2<int>;

        class [[LUA_USERTYPE_TEMPLATE(Vector, IntVector)]] Vec2 { };

### Exposing Functions

+ **LUA_CTOR**

    Exposes a usertype constructor or a static function as a factory.

+ **LUA_FUNC**

    Exposes a function and provides a few optional parameters.

    * `use_static` - Exposes the function with a static_cast
    * `$name = other` - Exposes the function with an alternative name
    * `arg = IData` - Exposes an argument with the specified type to TypeScriptToLua

+ **LUA_FUNC_OVERLOAD**

    Exposes an overloaded function. This is only necessary for an overloaded function with only one overload be exposed.

+ **LUA_FUNC_TEMPLATE**

    Exposes a templated function with the provided usertype group that is used in LUA_USERTYPE. This generates variations of the function with the usertypes in the group stripping off the group name.

        struct [[LUA_USERTYPE_NO_CTOR(Component)]] TransformComponent : public Component { };
        struct [[LUA_USERTYPE_NO_CTOR(Component)]] RenderComponent : public Component { };

        template<class T> [[LUA_FUNC_TEMPLATE(Component, ...)]] T& get() const;

        // Generates the following exposed functions
        TransformComponent& getTransform();
        RenderComponent& getRender();

+ **LUA_META_FUNC**

    Exposes a meta function such `index` or `addition` taking the meta function type as a parameter.
    https://sol2.readthedocs.io/en/latest/api/usertype.html#enumerations

        [[LUA_META_FUNC(index)]] sol::object index(const std::string& name, sol::this_state state);

+ **LUA_VAR**

    Exposes a variable.

+ **LUA_VAR_READONLY**

    Exposes a variable as readonly.

+ **LUA_PROPERTY_**

    Exposes a variable as a property with getter and/or setter. The following are some helper macros for using this feature.

        #define LUA_PROPERTY_GET(var) \
            [[LUA_PROPERTY_GET_]] auto get_##var() { return var; }
        #define LUA_PROPERTY_SET(var) \
            [[LUA_PROPERTY_SET_]] void set_##var(decltype(var) value) { var = value; }

        #define LUA_PROPERTY(var) \
            LUA_PROPERTY_GET(var) \
            LUA_PROPERTY_SET(var)
