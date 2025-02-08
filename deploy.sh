git pull
read -p "Enter new version number: " new_version
sed -i "s/<Version>.*<\/Version>/<Version>$new_version<\/Version>/" SimpleRssServer/SimpleRssServer.fsproj
git add SimpleRssServer/SimpleRssServer.fsproj
git commit -m "bump version number"
dotnet publish
sudo systemctl stop rssrdr-server.service
find /var/www/rss/* -not -name 'rss-cache' -not -path '/var/www/rss/rss-cache/*' -exec rm -rf {} +
cp -r SimpleRssServer/bin/Release/net8.0/publish/* /var/www/rss/
sudo systemctl start rssrdr-server.service
