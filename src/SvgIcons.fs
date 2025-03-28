module svgIcons

open Feliz

let bulbElectricEnergySvg (sz: int) =
    // https://www.svgrepo.com/svg/429021/bulb-electric-energy
    // LICENSE: CC0 License
    Svg.svg
        [ svg.xmlns "http://www.w3.org/2000/svg"
          svg.viewBox (0, 0, 96, 96)
          svg.width sz
          svg.height sz
          svg.fill "#000000"
          svg.id "bulbElectricEnergySvg"
          //svg.className (sprintf "w-%s h-%s" sz sz)
          svg.children
              [ Svg.title "bulb-electric-energy"
                Svg.path
                    [ svg.d
                          "M47.51,31.19l-1.2-.44A36,36,0,0,1,42,28.84c-2.92-1.57-5.64-6.52-5.94-7.08L35.15,20,37,19.55c.4-.11,4-.93,8.6,2.07s3.32,7.09,2.52,8.47Zm-9.35-9.8c1.05,1.83,3.05,4.75,4.8,5.69a31.46,31.46,0,0,0,3.62,1.63c.38-.95,1-3.44-2-5.42A10.19,10.19,0,0,0,38.16,21.39Z" ]
                Svg.path
                    [ svg.d
                          "M48.14,31.19l-.64-1.11A6.29,6.29,0,0,1,50,21.62c4.55-3,8.19-2.17,8.6-2.07L60.5,20l-.92,1.72c-.3.55-3,5.5-5.95,7.08a36.52,36.52,0,0,1-4.3,1.91Zm8.78-9.82a10.65,10.65,0,0,0-5.8,1.92c-3,2-2.44,4.46-2,5.42a33.08,33.08,0,0,0,3.62-1.63c1.76-.95,3.75-3.87,4.8-5.69Z" ]
                Svg.path
                    [ svg.d
                          "M47.25,49.74l-1.34-.49a51.71,51.71,0,0,1-5.72-2.54c-3.83-2.06-7.43-8.62-7.82-9.36l-1-1.92,2.09-.55c.21-.05,5.15-1.27,11.26,2.73C51.53,42.09,48,48.44,48,48.5Zm-12.93-13c1.34,2.42,4.25,6.82,6.82,8.21a47.39,47.39,0,0,0,5.19,2.32c.46-1,1.86-5-2.74-8S35.47,36.56,34.32,36.74Z" ]
                Svg.path
                    [ svg.d
                          "M48.4,49.74l-.71-1.24c0-.06-3.58-6.41,3.27-10.89,6-3.92,10.73-2.86,11.25-2.73l2.1.55-1,1.92c-.4.74-4,7.3-7.83,9.36a51.71,51.71,0,0,1-5.72,2.54ZM60,36.65a14.65,14.65,0,0,0-8,2.63c-4.59,3-3.2,7-2.74,8A47.39,47.39,0,0,0,54.5,45c2.57-1.39,5.48-5.79,6.83-8.21A9.49,9.49,0,0,0,60,36.65Z" ]
                Svg.path
                    [ svg.d
                          "M52.82,84.93H42.9a5.68,5.68,0,0,1-5.65-5.7V61.94a52,52,0,0,0-3.68-5.83c-4.52-6.73-11.35-16.9-12-23.54a23.74,23.74,0,0,1,6-18.88C32.66,8,40.62,4.33,47.82,4.33a28.42,28.42,0,0,1,21.29,9.94,22.13,22.13,0,0,1,5,18.37c-1.35,7.45-7.87,17.17-12.18,23.61a42.76,42.76,0,0,0-3.47,5.6V79.23A5.68,5.68,0,0,1,52.82,84.93Zm-5-78.6A27,27,0,0,0,29,15a21.82,21.82,0,0,0-5.5,17.33c.63,6.15,7.3,16.07,11.71,22.63,3,4.44,4,6,4,6.95V79.23a3.67,3.67,0,0,0,3.65,3.7h9.92a3.68,3.68,0,0,0,3.65-3.7V61.78c0-.92,1-2.43,3.8-6.64,4.22-6.3,10.59-15.82,11.87-22.85a20.14,20.14,0,0,0-4.57-16.73A26.37,26.37,0,0,0,47.82,6.33Z" ]
                Svg.path [ svg.d "M61,64.53H34.68a1,1,0,0,1,0-2H61a1,1,0,0,1,0,2Z" ]
                Svg.path
                    [ svg.d
                          "M48.86,25.73c-.27,3.15-.32,6.3-.28,9.45a37.31,37.31,0,0,0,.24,4.72c.2,1.58.47,3.16.52,4.73.18,3.15-.58,6.3-.65,9.45a93.13,93.13,0,0,0,.17,9.45,1,1,0,1,1-2,.16.43.43,0,0,1,0-.16A90.31,90.31,0,0,0,47,54.08c-.07-3.15-.82-6.3-.64-9.45,0-1.57.32-3.15.51-4.73a37.32,37.32,0,0,0,.25-4.72c0-3.15,0-6.3-.28-9.45a1,1,0,1,1,2-.17Z" ]
                Svg.path [ svg.d "M79.17,92H67.06a1,1,0,0,1,0-2H79.17a1,1,0,1,1,0,2Z" ]
                Svg.path [ svg.d "M63.24,92h-27a1,1,0,0,1,0-2h27a1,1,0,0,1,0,2Z" ]
                Svg.path [ svg.d "M31.38,92H27.7a1,1,0,0,1,0-2h3.68a1,1,0,1,1,0,2Z" ]
                Svg.path [ svg.d "M20,92H18.37a1,1,0,0,1,0-2H20a1,1,0,1,1,0,2Z" ]
                Svg.path [ svg.d "M45,70.44H38.25a1,1,0,0,1,0-2H45a1,1,0,0,1,0,2Z" ]
                Svg.path [ svg.d "M57.47,78.32H50.61a1,1,0,0,1,0-2h6.86a1,1,0,0,1,0,2Z" ]
                Svg.path
                    [ svg.d
                          "M47.81,92h-.12a5.25,5.25,0,0,1-5.25-5.25V82.93H53.06v3.82A5.25,5.25,0,0,1,47.81,92Zm-3.37-7.07v1.82A3.26,3.26,0,0,0,47.69,90h.12a3.26,3.26,0,0,0,3.25-3.25V84.93Z" ]
                Svg.path
                    [ svg.d
                          "M39.8,17.4a1,1,0,0,1-.95-.69l-1.69-5.08a1,1,0,0,1,1.9-.63l1.69,5.08a1,1,0,0,1-.63,1.26A1,1,0,0,1,39.8,17.4Z" ]
                Svg.path
                    [ svg.d
                          "M56.6,17.75a.85.85,0,0,1-.31-.06,1,1,0,0,1-.63-1.26l1.69-5.08a1,1,0,0,1,1.89.63l-1.69,5.08A1,1,0,0,1,56.6,17.75Z" ]
                Svg.path [ svg.d "M48,16.26a1,1,0,0,1-1-1V9.91a1,1,0,1,1,2,0v5.35A1,1,0,0,1,48,16.26Z" ] ] ]
