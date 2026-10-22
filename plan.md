my plan is to create a somewhat similar project as a bloomberg terminal

call this project "Sumire Platform"

use the icons, and some inspiration from sumire yoshizawa from persona 5 royal

GOAL:
i want it to be able to not only showcase the financial data, but able to see/check on stocks, give a prediction regarding those stocks,
but also to provide a clear forecast of the market trends. As well as utilization similar to the Bloomberg Terminal
i want this project to be able to provide a clear understanding regarding the stock, its perfomance, how the market is right now for that stock, how well it might look like, and all the import stats a trader would need
ensure to keep this up to date, so it gets the latest data as well
make this for IHSG for now

FInd inspiration on what a platform like this might look like, this is for personal use btw


UI:
similar to the bloomberg terminal, add some features/styles from sumire in persona 5 royal as well, maybe a small mascot on the side similar to her
ensure to keep it simple, clean, and user-friendly, usable in keyboard as well, but easy to remember and understand


now for the technical stuff:
BE : either going .NET, or anything that can handle these requirements, do not use python
ensure to keep it efficient and scalable, DO NOT OVERENGINEER

For anything requiring databases, utilize the supabase, i have sent the connection string as "SUPABASE_CONNECTION_STRING" in the .env file, please use that for any database connections. DO NOT READ THE VALUES OF IT THOUGH

ensure to keep it simple and not overengineer, do not save unnecessary data into the db, only important stuff

for models for forecasting/etc, either use pre-trained models or use market used techniques for this, do this as what a professional in the field would do and use
