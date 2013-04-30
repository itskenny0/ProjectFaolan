/*
Project Faolan a Simple and Free Server Emulator for Age of Conan.
Copyright (C) 2009, 2010 The Project Faolan Team

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program. If not, see <http://www.gnu.org/licenses/>.
*/

#include "WorldServer.h"

void WorldServer::HandleCommand(GlobalTable* GTable, string command, vector<GameClient*>* clientList)
{	
	GameClient* client = GTable->client;
	if(false) //!isGM(client->nClientInst))
	{
		// send 'you are not a gm' text
		return;
	}
	if(command[0] == '.' && command[1] == '.')
		return;

	if(command == ".shutdown")
	{
		exit(2);
	}
	else if(command == ".disconnect")
	{
		closesocket(client->clientSocket);
	}
	else if(command.substr(1, 8) == "teleport")
	{
		string map = command.substr(10, command.length() - 1);

		if(map == "")
			return;

		Log.Debug("Map: %s\n", map.c_str());
		map = String::replace(map, "'", "\\'");
		teleportChar(map, GTable->client, clientList);
	}
	else if(command.substr(0, 7) == "additem")
	{
		string item = command.substr(8, command.length() - 1);

		if(item == "")
			return;

		item = String::replace(item, "'", "\\'");
		addItem(item, client);	
	}
	else if(command.substr(0, 8) == "addmoney")
	{
		string money = command.substr(9, command.length() - 1);

		if(money == "")
			return;

		addMoney(atoi64(money.c_str()), client);
	}
	else if(command.substr(0, 7) == "ABlearn")
	{
		size_t s1=0,s2=0;
		s1 = command.find(' ',8);
		uint32 ability = atoi(command.substr(8, s1).c_str());
		uint32 posx = atoi(command.substr(s1, command.length()-1).c_str());

		if(ability == 0)
			return;
		
		addAbility(client,ability,client->charInfo.level,posx);
	}
	else if(command.substr(0, 6) == "Slearn")
	{
		string spellId = command.substr(7, command.length() - 1);
		
		for(int x = 0; x < GTable->SPELLS.size(); x++)
		{
			if(GTable->SPELLS[x].name == spellId)
			{
				spellId = String::toString(GTable->SPELLS[x].id);
			}
		}
		Log.Debug("Send Spell with id %s\n", spellId.c_str());
		addSpell(client, atoi64(spellId.c_str()));
	}
	else if(command.substr(0, 8) == "levelup ")
	{
		
		string lvl = command.substr(8, command.length() - 1);
		if(lvl == "")
			return;

		addCharLevel(lvl,client);
	}

	else
	{
		// unknown command
	}
}

void WorldServer::addItem(string itemname, GameClient* client)
{
	Item item_data;
	if(Database.getItemData(itemname.c_str(), &item_data))
	{
		uint8 item_pack[] =
		{ 
			0x00, 0x00, 0x00, 0x00, 
			0x20, 0xd4, 0x00, 0x05, 
			0xa1, 0xef, 0xf5, 0x7b, 
			0x00, 0x00, 0x13, 0xb5, 
			0x00, 0x00, 0x00, 0x00, 
			0x00, 0x00, 0x00, 0x01,
			0x00, 0x00, 0x00, 0x08, 
			0x00, 0x00, 0x00, 0x00, 
			0x00, 0x00, 0x00, 0x17, 
			0x00, 0x4f, 0x66, 0xbb, 
			0x00, 0x00, 0x00, 0x2e, 
			0x00, 0x00, 0x00, 0x0f
		};

		PacketBuffer aBuffer(500);
		aBuffer.writeHeader("GameCharAgent", "GameCharInterface", gameUnknown1, 0, client->nClientInst, 0, 0x1d); 
		aBuffer.write<uint32>(0x00000068);
		aBuffer.write<uint32>(client->nClientInst);
		aBuffer.writeArray(item_pack, sizeof(item_pack));
		aBuffer.write<uint32>(item_data.id);
		aBuffer.write<uint32>(item_data.id);
		aBuffer.write<uint32>(item_data.iLevel);
		aBuffer.write<uint32>(0x4f4f4939);//unk
		aBuffer.write<uint32>(item_data.data1);
		aBuffer.write<uint32>(item_data.data2);
		aBuffer.write<uint32>(item_data.data3);
		aBuffer.write<uint32>(item_data.data4);
		aBuffer.write<uint32>(item_data.data1);
		aBuffer.write<uint32>(item_data.data2);
		aBuffer.write<uint32>(item_data.data3);
		aBuffer.write<uint32>(item_data.data4);
		aBuffer.write<uint32>(0x000003f1);
		aBuffer.write<uint32>(0x00000000);
		aBuffer.doItAll(client->clientSocket);
		Log.Success("Command [additem]: Item [%s][%u] has been added to player %s!\n",itemname.c_str(),item_data.id,client->charInfo.name.c_str());
	}
	else
	{
		Log.Error("Error : Can't add item into player inventory!\n");
	}
}

void WorldServer::addMoney(uint32 cash, GameClient* client)
{
	PacketBuffer aBuffer(500);
	aBuffer.writeHeader("GameCharAgent", "GameCharInterface", gameUnknown1, 0, client->nClientInst, 0, 0x05); // PassBlob
	aBuffer.write<uint32>(0x00000000);
	aBuffer.write<uint32>(cash);//value 35g 35 00 00 00
	aBuffer.write<uint32>(0x00000002);// replace old money to new value 
	aBuffer.doItAll(client->clientSocket);
}

void WorldServer::addCharLevel(string level,GameClient *Client)
{
	WorldServer::increaseLevel(Client,atoi(level.c_str()));

}

void WorldServer::teleportChar(string zone, GameClient* client, vector<GameClient*>* clientList)
{
	/*
		peer1_316[] = {
0x00, 0x00, 0x00, 0x4e, 0xfd, 0xf4, 0x03, 0x53, 0x00, 0x00, 0x00, 0x17, 
0x0a, 0x07, 0x0d, 0x13, 0xce, 0x71, 0xb1, 0x10, 0x11, 
0x12, 0x0a, 0x0d, 0x47, 0xc1, 0x67, 0x6c, 0x10, 0xcd, 0xa0, 0xd2, 0x08, 
0x20, 0x00, 
0x00, 0x00, 0x00, 0x2b, 
0x10, 0xd2, 0x7b, 0xc0, 
0x00, 0x00, 0xc3, 0x50, 0x01, 0x14, 0x90, 0x4d, 
0x01, 0x62, 
0x00, 0x00, 0xc7, 0x9c, 
0x00, 0x00, 0x0f, 0xaa, 
0x00, 0x00, 0x00, 0x00, 
0x01, 0x14, 0x90, 0x4d, 
0x00, 
0x00, 0x00, 0x00, 0x00, 
0x00, 0x00, 0x00, 0x00, 
0x00, 0x00, 0x00, 0x00 };
	*/
	uint8 sender[] = { 0x0d, 0x13, 0xce, 0x71, 0xb1, 0x10, 0x0b };
	uint8 receiver[] = { 0x0d, 0x47, 0xc1, 0x67, 0x6c, 0x10, 0xe6, 0x8f, 0x80, 0x08 };
	teleport map;
	if(Database.getMapDetails(zone, &map))
	{
		client->charInfo.map = map.mapID;
		client->charInfo.position.x = map.position.x;
		client->charInfo.position.y = map.position.y;
		client->charInfo.position.z = map.position.z;
		client->charInfo.rotation.x = map.rotation.x;
		client->charInfo.rotation.y = map.rotation.y;
		client->charInfo.rotation.z = map.rotation.z;
		//Save new position
		Database.saveCharPosition(client);


		Log.Notice("Teleporting\nMapId: %i\nPOS X: %i\nPOS Y: %i\nPOS Z: %i\n\n", map.mapID, map.position.x, map.position.y, map.position.z);
		PacketBuffer aBuffer(500);
		aBuffer.writeHeader(sender, sizeof(sender), receiver, sizeof(receiver), 0x2000, true);
		uint32 size = (11 * 4) + 3;
		aBuffer.write<uint32>(size);
		aBuffer.write<uint32>(0x10d27bc0);
		aBuffer.write<uint32>(0x0000c350);
		aBuffer.write<uint32>(client->nClientInst);
		aBuffer.write<uint16>(0x0162);
		aBuffer.write<uint32>(0x0000c79c);
		aBuffer.write<uint32>(map.mapID);
		aBuffer.write<uint32>(0x00000000);
		aBuffer.write<uint32>(client->nClientInst);
		aBuffer.write<uint8>(0x00);
		aBuffer.write<uint32>(0x00000000);
		aBuffer.write<uint32>(0x00000000);
		aBuffer.write<uint32>(0x00000000);
		aBuffer.write<uint32>(0x3e4f4f3c);
		aBuffer.doItAll(client->clientSocket);

		uint8 pack2_0[] = 
		{ 
			
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x3f, 0x80, 0x00, 0x00, 0x62, 0x00, 0x00, 0xc7, 0x9c
		};
		uint8 pack2_1[] = 
		{ 
			0x00, 0x00, 0x00, 0x0f, 
			0x00, 0x00, 0x9c, 0x50, 
			0x00, 0x28, 0x3a, 0x0c, //instance
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x14, 0x00, 0x01, 0x87, 0x04
		};
		/*
		peer1_317[] = {
0x00, 0x00, 0x00, 0x8a, 0x94, 0xc8, 0x93, 0x92, 0x00, 0x00, 0x00, 0x17, 0x0a, 0x07, 0x0d, 0x13, 0xce, 0x71, 0xb1, 0x10, 0x11, 0x12, 0x0a, 0x0d, 0x47, 0xc1, 0x67, 0x6c, 0x10, 0xcd, 0xa0, 0xd2, 0x08, 
0x20, 0x00, 
0x00, 0x00, 0x00, 0x67, 
0x5a, 0x32, 0xf0, 0xd7, 
0x00, 0x00, 0xc3, 0x50, 0x01, 0x14, 0x90, 0x4d, 
0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x3f, 0x80, 0x00, 0x00, 0x62, 0x00, 0x00, 0xc7, 0x9c, 
0x00, 0x00, 0x0f, 0xaa, 
0x00, 0x00, 0x00, 0x00, 0x01, 0x14, 0x90, 0x4d, 0x00, 0x00, 0x00, 0x0f, 0x00, 0x00, 0x9c, 0x50, 0x00, 0x28, 0x3a, 0x0c, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x14, 0x00, 0x01, 0x87, 0x04, 
0x43, 0xa1, 0x40, 0x00, 
0x43, 0x16, 0x00, 0x00, 
0x44, 0x30, 0x39, 0x9a, 
0x00, 0x00, 0x00, 0x32, 0x00 };
		*/
		aBuffer = PacketBuffer(500);
		aBuffer.writeHeader(sender, sizeof(sender), receiver, sizeof(receiver), 0x2000, true);
		size = sizeof(pack2_0) + sizeof(pack2_1) + (11 * 4) + 1;
		aBuffer.write<uint32>(size);
		aBuffer.write<uint32>(0x5a32f0d7);
		aBuffer.write<uint32>(0x0000c350);
		aBuffer.write<uint32>(client->nClientInst);
		aBuffer.writeArray(pack2_0,sizeof(pack2_0));
		aBuffer.write<uint32>(client->charInfo.map);
		aBuffer.write<uint32>(0);
		aBuffer.write<uint32>(client->nClientInst);
		aBuffer.writeArray(pack2_1,sizeof(pack2_1));
		aBuffer.write<uint32>(client->charInfo.position.x);
		aBuffer.write<uint32>(client->charInfo.position.y);
		aBuffer.write<uint32>(client->charInfo.position.z);
		aBuffer.write<uint32>(0x00000032);
		aBuffer.write<uint8>(0);
		aBuffer.doItAll(client->clientSocket);

		uint32 i_nDimID = (client->nClientInst >> 24) & 0x000000ff;
		RealmInfo* realm = 0;
		for(uint32 i = 0; i < Settings.realms.size(); i++)
		{
			if(Settings.realms[i]->realmID == i_nDimID)
			{
				realm = Settings.realms[i];
				break; // break forloop
			}
		}

		/*
		peer1_318[] = {
0x00, 0x00, 0x00, 0x25, 0xc1, 0x23, 0x73, 0xbf, 0x00, 0x00, 0x00, 0x17, 0x0a, 0x07, 0x0d, 0x13, 0xce, 0x71, 0xb1, 0x10, 0x11, 0x12, 0x0a, 0x0d, 0x47, 0xc1, 0x67, 0x6c, 0x10, 0xcd, 0xa0, 0xd2, 0x08, 
0x20, 0x02, 
0x25, 0x12, 0xd0, 0x8d, 0x1b, 0x64 };
		*/
		aBuffer = PacketBuffer(500);
		aBuffer.writeHeader(sender, sizeof(sender), receiver, sizeof(receiver), 0x2002, true);
		aBuffer.write<uint32>(htonl(inet_addr(realm->worldServerIPAddress.c_str()))); //World-Server Adress
		aBuffer.write<uint16>(realm->worldServerPort); //WorldServer Port
		aBuffer.doItAll(client->clientSocket);
	}
}